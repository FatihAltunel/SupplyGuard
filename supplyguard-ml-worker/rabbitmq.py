"""RabbitMQ lifecycle, topology, delivery handling, and process-local deduplication."""

import asyncio
import logging
from collections import OrderedDict
from contextlib import suppress
from time import monotonic
from uuid import UUID

import aio_pika
from aio_pika.abc import (
    AbstractExchange,
    AbstractIncomingMessage,
    AbstractQueue,
    AbstractRobustChannel,
    AbstractRobustConnection,
)
from aio_pika.connection import make_url
from aio_pika.exceptions import CONNECTION_EXCEPTIONS
from pydantic import ValidationError

from config import Settings
from contracts import COMPLETED_TYPE, REQUEST_TYPE, RiskAnalysisCompletedEvent, RiskAnalysisRequestedEvent
from inference import MockInference

logger = logging.getLogger("uvicorn.error")


class ProcessedCorrelations:
    """Bounded FIFO cache. Callers serialize access through the delivery lock."""

    def __init__(self, max_entries: int, ttl_seconds: float) -> None:
        self._max_entries = max_entries
        self._ttl_seconds = ttl_seconds
        self._entries: OrderedDict[UUID, float] = OrderedDict()

    def _expire(self) -> None:
        now = monotonic()
        while self._entries and next(iter(self._entries.values())) <= now:
            self._entries.popitem(last=False)

    def contains(self, correlation_id: UUID) -> bool:
        self._expire()
        return correlation_id in self._entries

    def add(self, correlation_id: UUID) -> None:
        self._expire()
        self._entries.pop(correlation_id, None)
        self._entries[correlation_id] = monotonic() + self._ttl_seconds
        while len(self._entries) > self._max_entries:
            self._entries.popitem(last=False)


class RabbitMQWorker:
    def __init__(self, settings: Settings, inference: MockInference) -> None:
        self._settings = settings
        self._inference = inference
        self._connection: AbstractRobustConnection | None = None
        self._channel: AbstractRobustChannel | None = None
        self._task: asyncio.Task[None] | None = None
        self._delivery_lock = asyncio.Lock()
        self._processed = ProcessedCorrelations(settings.dedup_max_entries, settings.dedup_ttl_seconds)

    @property
    def is_ready(self) -> bool:
        return (
            self._connection is not None
            and not self._connection.is_closed
            and self._connection.connected.is_set()
            and self._channel is not None
            and not self._channel.is_closed
            and self._task is not None
            and not self._task.done()
        )

    @property
    def inference_ready(self) -> bool:
        return self._inference.is_ready

    async def _connect_with_retry(self) -> AbstractRobustConnection:
        settings = self._settings
        delay = settings.retry_initial_seconds
        while True:
            # Own each attempt so failed/cancelled connections are explicitly closed.
            connection = aio_pika.RobustConnection(
                make_url(
                    host=settings.host,
                    port=settings.port,
                    login=settings.user,
                    password=settings.password.get_secret_value(),
                    virtualhost=settings.vhost,
                    ssl=settings.use_tls,
                ),
                fail_fast=True,
                reconnect_interval=settings.retry_initial_seconds,
            )
            try:
                await connection.connect(timeout=settings.connection_timeout_seconds)
                return connection
            except asyncio.CancelledError:
                await connection.close()
                raise
            except CONNECTION_EXCEPTIONS as exception:
                await connection.close()
                logger.warning(
                    "RabbitMQ connection failed (%s); retrying in %.1fs.",
                    type(exception).__name__,
                    delay,
                )
                await asyncio.sleep(delay)
                delay = min(delay * 2, settings.retry_max_seconds)
            except BaseException:
                await connection.close()
                raise

    async def start(self) -> None:
        if self._connection is not None:
            raise RuntimeError("RabbitMQ worker is already started.")
        self._connection = await self._connect_with_retry()
        try:
            self._channel = await self._connection.channel(publisher_confirms=True, on_return_raises=True)
            await self._channel.set_qos(prefetch_count=self._settings.prefetch_count)
            exchange = await self._channel.declare_exchange(
                self._settings.exchange, aio_pika.ExchangeType.TOPIC, durable=True
            )
            errors = await self._channel.declare_exchange(
                self._settings.error_exchange, aio_pika.ExchangeType.DIRECT, durable=True
            )
            invalid = await self._channel.declare_queue(self._settings.error_queue, durable=True)
            await invalid.bind(errors, routing_key=REQUEST_TYPE)
            requests = await self._channel.declare_queue(
                self._settings.request_queue,
                durable=True,
                arguments={
                    "x-dead-letter-exchange": self._settings.error_exchange,
                    "x-dead-letter-routing-key": REQUEST_TYPE,
                },
            )
            await requests.bind(exchange, routing_key=REQUEST_TYPE)
            completions = await self._channel.declare_queue(self._settings.completed_queue, durable=True)
            await completions.bind(exchange, routing_key=COMPLETED_TYPE)
            self._task = asyncio.create_task(self._consume(requests, exchange), name="risk-analysis-consumer")
        except BaseException:
            await self.stop()
            raise

    async def stop(self) -> None:
        task, self._task = self._task, None
        try:
            if task is not None:
                task.cancel()
                with suppress(asyncio.CancelledError):
                    await task
        finally:
            connection, self._connection = self._connection, None
            self._channel = None
            if connection is not None:
                # The broker requeues any outstanding unacknowledged delivery.
                await connection.close()

    async def _consume(self, queue: AbstractQueue, exchange: AbstractExchange) -> None:
        async with queue.iterator() as messages:
            async for message in messages:
                try:
                    await self.handle_message(message, exchange)
                except Exception as exception:
                    logger.warning(
                        "Delivery interrupted; request will be redelivered (%s).",
                        type(exception).__name__,
                    )
                    await asyncio.sleep(1)

    async def handle_message(self, message: AbstractIncomingMessage, exchange: AbstractExchange) -> None:
        async with message.process(requeue=True, ignore_processed=True):
            try:
                request = RiskAnalysisRequestedEvent.model_validate_json(
                    message.body, by_alias=True, by_name=False
                )
                if (
                    message.routing_key != REQUEST_TYPE
                    or message.type != request.message_type
                    or message.message_id != str(request.message_id)
                    or message.correlation_id != str(request.correlation_id)
                    or message.content_type != "application/json"
                    or message.content_encoding != "utf-8"
                ):
                    raise ValueError("AMQP envelope does not match the JSON contract.")
            except (ValidationError, ValueError):
                logger.warning("Rejected invalid risk-analysis contract to the dead-letter queue.")
                await message.reject(requeue=False)
                return

            # Serialize check -> inference -> confirmed publish -> cache insertion.
            # This also protects against concurrent deliveries of one correlation ID.
            async with self._delivery_lock:
                if self._processed.contains(request.correlation_id):
                    return  # process() ACKs without inference or publication.
                completion = await self._inference.analyze(request)
                await self._publish(completion, exchange)
                self._processed.add(request.correlation_id)
            # Cache insertion precedes the ACK: an ACK failure is safe to redeliver.

    async def _publish(self, completion: RiskAnalysisCompletedEvent, exchange: AbstractExchange) -> None:
        # Publication failures must redeliver, never become terminal inference errors.
        confirmation = await exchange.publish(
            aio_pika.Message(
                body=completion.model_dump_json(by_alias=True).encode("utf-8"),
                content_type="application/json",
                content_encoding="utf-8",
                delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                message_id=str(completion.message_id),
                correlation_id=str(completion.correlation_id),
                type=completion.message_type,
                timestamp=completion.occurred_at_utc,
            ),
            routing_key=COMPLETED_TYPE,
            mandatory=True,
            timeout=self._settings.publish_timeout_seconds,
        )
        if confirmation is None:
            raise RuntimeError("Result publication requires publisher confirms.")
