"""Run with: python -m unittest discover -s tests -v (from the worker directory)."""

import asyncio
import copy
import json
import os
import sys
import unittest
from contextlib import asynccontextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
from uuid import UUID, uuid4

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import inference
import main
import rabbitmq
from config import Settings
from contracts import COMPLETED_TYPE, REQUEST_TYPE, RiskAnalysisCompletedEvent, RiskAnalysisRequestedEvent
from fastapi import FastAPI, Request
from pydantic import ValidationError

REQUEST = {
    "messageId": "71ddac07-fae2-4729-92f6-064e57ee9b01",
    "messageType": REQUEST_TYPE,
    "correlationId": "f30f4f56-45f2-4c8d-92ac-b069dbf48761",
    "occurredAtUtc": "2026-10-27T09:00:00.000Z",
    "supplierId": "e028a42b-10d1-4911-b69b-dfc29986ce12",
    "input": {
        "featureSchemaVersion": 1,
        "observedAtUtc": "2026-10-26T18:00:00.000Z",
        "features": {"averageDeliveryDelayDays": 8.0, "onTimeDeliveryRate": 0.72},
    },
}


def parse(data):
    return RiskAnalysisRequestedEvent.model_validate_json(json.dumps(data), by_alias=True, by_name=False)


def settings(**overrides):
    return Settings(password="test-only", _env_file=None, **overrides)


class Incoming:
    """Model the ACK/reject boundary of aio-pika's process context manager."""

    def __init__(self, data=None, fail_ack=False):
        data = REQUEST if data is None else data
        self.body = json.dumps(data).encode()
        self.routing_key = REQUEST_TYPE
        self.type = REQUEST_TYPE
        self.message_id = data.get("messageId")
        self.correlation_id = data.get("correlationId")
        self.content_type = "application/json"
        self.content_encoding = "utf-8"
        self.processed = False
        self.fail_ack = fail_ack
        self.actions = []

    async def reject(self, requeue):
        self.actions.append(("reject", requeue))
        self.processed = True

    @asynccontextmanager
    async def process(self, requeue=False, ignore_processed=False):
        try:
            yield self
        except BaseException:
            if not self.processed:
                await self.reject(requeue)
            raise
        else:
            if not self.processed:
                if self.fail_ack:
                    raise ConnectionError("Simulated lost request ACK.")
                self.actions.append(("ack",))
                self.processed = True


class ContractTests(unittest.TestCase):
    def test_request_roundtrip(self):
        self.assertEqual(json.loads(parse(REQUEST).model_dump_json()), REQUEST)

    def test_strict_wire_validation(self):
        invalid_values = {
            "supplierId": [str(UUID(int=0)), REQUEST["supplierId"].upper(), REQUEST["supplierId"].replace("-", "")],
            "occurredAtUtc": ["2026-10-27T09:00:00Z", "2026-10-27T09:00:00.000+00:00", 1793091600],
            "messageType": ["risk-analysis.requested.v2"],
        }
        for field, values in invalid_values.items():
            for value in values:
                data = copy.deepcopy(REQUEST)
                data[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValidationError):
                    parse(data)
        for value in (True, 1.0, "1", 2):
            data = copy.deepcopy(REQUEST)
            data["input"]["featureSchemaVersion"] = value
            with self.subTest(version=value), self.assertRaises(ValidationError):
                parse(data)
        for value in (True, "8", [], {}, float("nan"), float("inf")):
            data = copy.deepcopy(REQUEST)
            data["input"]["features"]["invalid"] = value
            with self.subTest(feature=value), self.assertRaises(ValidationError):
                parse(data)
        data = copy.deepcopy(REQUEST)
        data["supplier_id"] = data.pop("supplierId")
        with self.assertRaises(ValidationError):
            parse(data)

    def test_generic_feature_numbers_and_null(self):
        data = copy.deepcopy(REQUEST)
        data["input"]["features"] = {"custom": 7, "missing": None}
        self.assertEqual(parse(data).input.features, {"custom": 7.0, "missing": None})


class SettingsTests(unittest.TestCase):
    def test_environment_parsing_and_secret_masking(self):
        with patch.dict(os.environ, {
            "RABBITMQ_PASSWORD": "sensitive-test-secret",
            "RABBITMQ_PORT": "5673",
            "RABBITMQ_REQUEST_QUEUE": "custom.requests",
            "RABBITMQ_DEDUP_MAX_ENTRIES": "25",
        }, clear=True):
            config = Settings(_env_file=None)
        self.assertEqual(config.port, 5673)
        self.assertEqual(config.request_queue, "custom.requests")
        self.assertEqual(config.dedup_max_entries, 25)
        self.assertNotIn("sensitive-test-secret", repr(config))

    def test_invalid_configuration(self):
        for values in ({"port": 0}, {"dedup_max_entries": 0}, {"dedup_ttl_seconds": 0},
                       {"retry_initial_seconds": 5, "retry_max_seconds": 1},
                       {"request_queue": "same", "completed_queue": "same"}):
            with self.subTest(values=values), self.assertRaises(ValidationError):
                settings(**values)
        with patch.dict(os.environ, {}, clear=True), self.assertRaises(ValidationError):
            Settings(_env_file=None)


class CacheTests(unittest.TestCase):
    def test_expiry_and_capacity(self):
        cache = rabbitmq.ProcessedCorrelations(max_entries=2, ttl_seconds=10)
        first, second, third = uuid4(), uuid4(), uuid4()
        with patch.object(rabbitmq, "monotonic", return_value=0):
            cache.add(first)
            cache.add(second)
            self.assertTrue(cache.contains(first))
            cache.add(third)
            self.assertFalse(cache.contains(first))
            self.assertTrue(cache.contains(second))
        with patch.object(rabbitmq, "monotonic", return_value=10):
            self.assertFalse(cache.contains(second))
            self.assertFalse(cache.contains(third))


class WorkerTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.engine = inference.MockInference()
        await self.engine.initialize()
        self.worker = rabbitmq.RabbitMQWorker(settings(), self.engine)
        with patch.object(inference.asyncio, "sleep", new=AsyncMock()):
            self.completion = await self.engine.analyze(parse(REQUEST))
        self.exchange = SimpleNamespace(publish=AsyncMock(return_value=object()))

    async def test_nonblocking_two_second_inference_and_exact_result(self):
        task = asyncio.create_task(self.engine.analyze(parse(REQUEST)))
        await asyncio.sleep(0.05)
        self.assertFalse(task.done())
        event = await task
        self.assertGreaterEqual(event.execution.latency_ms, 1950)
        self.assertEqual(event.result, inference.dummy_result())
        self.assertEqual(event.result.risk_score, 65.0)
        self.assertEqual(event.status, "succeeded")
        self.assertIsNone(event.error)
        body = json.loads(event.model_dump_json())
        self.assertEqual(body["messageType"], COMPLETED_TYPE)
        self.assertEqual(body["requestMessageId"], REQUEST["messageId"])
        for timestamp in (body["occurredAtUtc"], *[body["execution"][key] for key in ("startedAtUtc", "completedAtUtc")]):
            self.assertRegex(timestamp, r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$")
        self.assertEqual(RiskAnalysisCompletedEvent.model_validate_json(event.model_dump_json()), event)

    async def test_readiness_lifecycle(self):
        self.assertTrue(self.engine.is_ready)
        await self.engine.close()
        self.assertFalse(self.engine.is_ready)
        with self.assertRaises(RuntimeError):
            await self.engine.analyze(parse(REQUEST))

    async def test_terminal_failure_is_sanitized_and_deduplicated(self):
        with patch.object(inference.asyncio, "sleep", new=AsyncMock()), \
             patch.object(inference, "dummy_result", side_effect=RuntimeError("private-data")) as compute:
            await self.worker.handle_message(Incoming(), self.exchange)
            await self.worker.handle_message(Incoming(), self.exchange)
        self.assertEqual(compute.call_count, 3)
        self.exchange.publish.assert_awaited_once()
        body = json.loads(self.exchange.publish.call_args.args[0].body)
        self.assertEqual(body["status"], "failed")
        self.assertIsNone(body["result"])
        self.assertEqual(body["error"]["code"], "DUMMY_PROCESSING_FAILED")
        self.assertNotIn("private-data", json.dumps(body))

    async def test_duplicate_skips_inference_and_publication(self):
        first, duplicate = Incoming(), Incoming()
        async def publish(*args, **kwargs):
            self.assertEqual(first.actions, [])
            return object()
        self.exchange.publish.side_effect = publish
        with patch.object(self.engine, "analyze", new=AsyncMock(return_value=self.completion)) as analyze:
            await self.worker.handle_message(first, self.exchange)
            await self.worker.handle_message(duplicate, self.exchange)
        analyze.assert_awaited_once()
        self.exchange.publish.assert_awaited_once()
        self.assertEqual(first.actions, [("ack",)])
        self.assertEqual(duplicate.actions, [("ack",)])

    async def test_concurrent_duplicates_only_publish_once(self):
        with patch.object(self.engine, "analyze", new=AsyncMock(return_value=self.completion)) as analyze:
            await asyncio.gather(*[self.worker.handle_message(Incoming(), self.exchange) for _ in range(5)])
        analyze.assert_awaited_once()
        self.exchange.publish.assert_awaited_once()

    async def test_failed_publication_does_not_poison_cache(self):
        self.exchange.publish.side_effect = [TimeoutError(), object()]
        first, redelivery = Incoming(), Incoming()
        with patch.object(self.engine, "analyze", new=AsyncMock(return_value=self.completion)) as analyze:
            with self.assertRaises(TimeoutError):
                await self.worker.handle_message(first, self.exchange)
            await self.worker.handle_message(redelivery, self.exchange)
        self.assertEqual(analyze.await_count, 2)
        self.assertEqual(first.actions, [("reject", True)])
        self.assertEqual(redelivery.actions, [("ack",)])

    async def test_missing_publisher_confirm_does_not_poison_cache(self):
        self.exchange.publish.return_value = None
        with patch.object(self.engine, "analyze", new=AsyncMock(return_value=self.completion)):
            with self.assertRaises(RuntimeError):
                await self.worker.handle_message(Incoming(), self.exchange)
        self.assertFalse(self.worker._processed.contains(parse(REQUEST).correlation_id))

    async def test_ack_failure_does_not_repeat_confirmed_publication(self):
        with patch.object(self.engine, "analyze", new=AsyncMock(return_value=self.completion)) as analyze:
            with self.assertRaises(ConnectionError):
                await self.worker.handle_message(Incoming(fail_ack=True), self.exchange)
            redelivery = Incoming()
            await self.worker.handle_message(redelivery, self.exchange)
        analyze.assert_awaited_once()
        self.exchange.publish.assert_awaited_once()
        self.assertEqual(redelivery.actions, [("ack",)])

    async def test_invalid_message_dead_lettered(self):
        incoming = Incoming({})
        await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("reject", False)])
        self.exchange.publish.assert_not_awaited()
        incoming = Incoming()
        incoming.correlation_id = "incorrect"
        await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("reject", False)])

    async def test_shutdown_cancellation_is_not_a_terminal_failure(self):
        incoming = Incoming()
        with patch.object(self.engine, "analyze", side_effect=asyncio.CancelledError):
            with self.assertRaises(asyncio.CancelledError):
                await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("reject", True)])
        self.exchange.publish.assert_not_awaited()
        self.assertFalse(self.worker._processed.contains(parse(REQUEST).correlation_id))

    async def test_startup_retry_backoff_and_attempt_cleanup(self):
        connections = [SimpleNamespace(connect=AsyncMock(side_effect=ConnectionError), close=AsyncMock()) for _ in range(3)]
        success = SimpleNamespace(connect=AsyncMock(), close=AsyncMock())
        worker = rabbitmq.RabbitMQWorker(settings(retry_initial_seconds=1, retry_max_seconds=2), self.engine)
        with patch.object(rabbitmq.aio_pika, "RobustConnection", side_effect=[*connections, success]), \
             patch.object(rabbitmq.asyncio, "sleep", new=AsyncMock()) as sleep:
            self.assertIs(await worker._connect_with_retry(), success)
        self.assertEqual([call.args[0] for call in sleep.await_args_list], [1, 2, 2])
        for connection in connections:
            connection.close.assert_awaited_once()
        success.close.assert_not_awaited()

    async def test_cancelled_connection_attempt_is_closed(self):
        connection = SimpleNamespace(connect=AsyncMock(side_effect=asyncio.CancelledError), close=AsyncMock())
        with patch.object(rabbitmq.aio_pika, "RobustConnection", return_value=connection):
            with self.assertRaises(asyncio.CancelledError):
                await self.worker._connect_with_retry()
        connection.close.assert_awaited_once()

    async def test_topology_and_worker_cleanup(self):
        channel = SimpleNamespace(
            is_closed=False,
            set_qos=AsyncMock(),
            declare_exchange=AsyncMock(return_value=self.exchange),
            declare_queue=AsyncMock(side_effect=[SimpleNamespace(bind=AsyncMock()) for _ in range(3)]),
        )
        connection = SimpleNamespace(
            is_closed=False, connected=asyncio.Event(), channel=AsyncMock(return_value=channel), close=AsyncMock()
        )
        connection.connected.set()
        with patch.object(self.worker, "_connect_with_retry", new=AsyncMock(return_value=connection)):
            await self.worker.start()
        self.assertTrue(self.worker.is_ready)
        connection.channel.assert_awaited_once_with(publisher_confirms=True, on_return_raises=True)
        declared = channel.declare_queue.await_args_list
        self.assertEqual(declared[1].kwargs["arguments"]["x-dead-letter-exchange"], "supplyguard.ml.errors")
        self.assertEqual(declared[2].args[0], "risk-analysis.completed.queue")
        connection.connected.clear()
        self.assertFalse(self.worker.is_ready)
        await self.worker.stop()
        connection.close.assert_awaited_once()
        self.assertFalse(self.worker.is_ready)

    async def test_topology_failure_closes_connection(self):
        connection = SimpleNamespace(channel=AsyncMock(side_effect=RuntimeError), close=AsyncMock())
        with patch.object(self.worker, "_connect_with_retry", new=AsyncMock(return_value=connection)):
            with self.assertRaises(RuntimeError):
                await self.worker.start()
        connection.close.assert_awaited_once()

    async def test_health_covers_broker_and_inference_readiness(self):
        app = FastAPI()
        request = Request({"type": "http", "app": app})
        for broker_ready, inference_ready in ((False, False), (False, True), (True, False), (True, True)):
            app.state.worker = SimpleNamespace(is_ready=broker_ready, inference_ready=inference_ready)
            response = await main.health(request)
            self.assertEqual(response.status_code, 200 if broker_ready and inference_ready else 503)

    async def test_lifespan_starts_and_stops_components(self):
        worker = SimpleNamespace(start=AsyncMock(), stop=AsyncMock())
        engine = SimpleNamespace(initialize=AsyncMock(), close=AsyncMock())
        app = FastAPI()
        with patch.object(main, "Settings", return_value=settings()), \
             patch.object(main, "MockInference", return_value=engine), \
             patch.object(main, "RabbitMQWorker", return_value=worker):
            async with main.lifespan(app):
                self.assertIs(app.state.worker, worker)
                engine.initialize.assert_awaited_once()
                worker.start.assert_awaited_once()
        worker.stop.assert_awaited_once()
        engine.close.assert_awaited_once()
        self.assertIsNone(app.state.worker)


if __name__ == "__main__":
    unittest.main()
