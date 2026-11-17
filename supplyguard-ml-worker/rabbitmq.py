"""Consume requests; ACK only after a confirmed response publication."""
import asyncio
import json
import logging
from contextlib import suppress
from typing import Any

import aio_pika
from aio_pika.abc import AbstractExchange, AbstractIncomingMessage, AbstractRobustConnection

from config import Settings
from schemas import RiskAnalysisRequest, RiskAnalysisResponse
from services import RiskAnalysisService

logger = logging.getLogger(__name__)


class RabbitMQWorker:
    def __init__(self, settings: Settings, service: RiskAnalysisService) -> None:
        self.settings = settings
        self.service = service
        self._connection: AbstractRobustConnection | None = None
        self._task: asyncio.Task[None] | None = None
        self._consuming = False

    @property
    def inference_ready(self) -> bool:
        return self.service.is_ready

    @property
    def is_ready(self) -> bool:
        return bool(
            self._consuming and self._task and not self._task.done()
            and self._connection and not self._connection.is_closed
            and self._connection.connected.is_set()
        )

    async def start(self) -> None:
        if self._task is not None:
            raise RuntimeError("Worker already started.")
        self._task = asyncio.create_task(self._run(), name="risk-analysis-consumer")

    async def stop(self) -> None:
        if self._task is not None:
            self._task.cancel()
            with suppress(asyncio.CancelledError):
                await self._task
            self._task = None

    async def _run(self) -> None:
        while True:
            try:
                connection = await aio_pika.connect_robust(
                    self.settings.rabbitmq_url.get_secret_value(), timeout=10
                )
                async with connection:
                    self._connection = connection
                    channel = await connection.channel(
                        publisher_confirms=True, on_return_raises=True
                    )
                    await channel.set_qos(prefetch_count=1)
                    await channel.declare_queue(self.settings.rabbitmq_response_queue, durable=True)
                    queue = await channel.declare_queue(
                        self.settings.rabbitmq_request_queue, durable=True
                    )
                    async with queue.iterator() as messages:
                        self._consuming = True
                        async for message in messages:
                            try:
                                await self.handle_message(message, channel.default_exchange)
                            except Exception as exception:
                                logger.warning("Delivery interrupted (%s); retrying.", type(exception).__name__)
                                await asyncio.sleep(self.settings.rabbitmq_retry_seconds)
            except asyncio.CancelledError:
                raise
            except Exception as exception:
                logger.warning("RabbitMQ unavailable (%s); reconnecting.", type(exception).__name__)
                await asyncio.sleep(self.settings.rabbitmq_retry_seconds)
            finally:
                self._consuming = False
                self._connection = None

    async def handle_message(
        self, message: AbstractIncomingMessage, exchange: AbstractExchange
    ) -> None:
        # Publication failure/cancellation requeues. A confirmed failure response
        # consumes malformed requests normally so they cannot block the queue.
        async with message.process(requeue=True):
            payload: Any = None
            try:
                payload = json.loads(message.body)
                request = RiskAnalysisRequest.model_validate(payload)
                response = await self.service.analyze(request)
            except (ValueError, UnicodeError) as exception:
                logger.warning("Request or inference validation failed (%s).", type(exception).__name__)
                response = self._failure(payload, message, "Geçersiz istek veya model çıktısı.")
            except Exception as exception:
                logger.exception("Risk analysis failed (%s).", type(exception).__name__)
                response = self._failure(payload, message, "Risk analizi tamamlanamadı.")

            # A disconnected broker cannot accept an error response either.
            confirmation = await exchange.publish(
                aio_pika.Message(
                    body=response.model_dump_json().encode("utf-8"),
                    content_type="application/json", content_encoding="utf-8",
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                    correlation_id=(
                        response.CorrelationId
                        if len(response.CorrelationId.encode("utf-8")) <= 255 else None
                    ),
                ),
                routing_key=self.settings.rabbitmq_response_queue,
                mandatory=True, timeout=self.settings.rabbitmq_publish_timeout,
            )
            if confirmation is None or confirmation is False:
                raise RuntimeError("Response publication was not confirmed.")

    @staticmethod
    def _failure(
        payload: Any, message: AbstractIncomingMessage, error: str
    ) -> RiskAnalysisResponse:
        data = payload if isinstance(payload, dict) else {}
        correlation = data.get("CorrelationId")
        supplier = data.get("SupplierId")
        return RiskAnalysisResponse(
            CorrelationId=correlation if isinstance(correlation, str) else (message.correlation_id or ""),
            SupplierId=supplier if isinstance(supplier, str) else "",
            ConfidenceScore=0.0, ExplanationText="", IsSuccessful=False, ErrorMessage=error,
        )
