"""Run: python -m unittest discover -s tests -v (from the worker directory)."""
import asyncio
import json
import sys
import unittest
from contextlib import asynccontextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import numpy as np
import shap
from fastapi import FastAPI, Request
from lightgbm import LGBMClassifier
from pydantic import ValidationError

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import main
import rabbitmq
import services
from config import Settings
from schemas import FEATURE_NAMES, RiskAnalysisRequest, RiskAnalysisResponse
from services import RiskAnalysisService, format_feature, risk_base_value, risk_shap_values

REQUEST = {
    "CorrelationId": "request-123", "SupplierId": "supplier-456",
    "averageDeliveryDelayDays": 2.4, "onTimeDeliveryRate": 0.95,
    "qualityDefectRate": 0.004, "DeliveryDelayTrend_Percentage": 15.2,
    "FinancialStabilityScore": 80.0, "ActiveRiskIndicatorsCount": 3.0,
}


def engine(probability=0.996, values=None):
    classifier = SimpleNamespace(
        classes_=np.array([0, 1]), feature_name_=list(FEATURE_NAMES),
        predict_proba=Mock(return_value=[[1 - probability, probability]]),
    )
    values = [[0.6, -0.5, 0.4, 0.2, -0.1, 0.0]] if values is None else values
    explainer = SimpleNamespace(shap_values=Mock(return_value=np.asarray(values)), expected_value=0.3)
    return RiskAnalysisService(object(), classifier, explainer)


class Incoming:
    def __init__(self, payload=REQUEST, body=None):
        self.body = json.dumps(payload).encode() if body is None else body
        self.correlation_id = "amqp-fallback"
        self.actions = []

    @asynccontextmanager
    async def process(self, requeue=False):
        try:
            yield self
        except BaseException:
            self.actions.append(("reject", requeue))
            raise
        else:
            self.actions.append(("ack",))


class SchemaAndFormattingTests(unittest.TestCase):
    def test_exact_wire_keys_and_finite_numbers(self):
        self.assertEqual(RiskAnalysisRequest(**REQUEST).model_dump(), REQUEST)
        for value in (True, "2.4", None, float("nan"), float("inf")):
            with self.subTest(value=value), self.assertRaises(ValidationError):
                RiskAnalysisRequest(**{**REQUEST, "averageDeliveryDelayDays": value})
        with self.assertRaises(ValidationError):
            RiskAnalysisRequest(**{**REQUEST, "unexpected": 1})

    def test_feature_units(self):
        expected = (
            "Ortalama Teslimat Gecikmesi (2.4 gün)",
            "Zamanında Teslimat Oranı (%95.0)", "Kalite Hata Oranı (%0.4)",
            "Gecikme Trendi (+%15.2)", "Finansal İstikrar Skoru (80.0)",
            "Aktif Risk Göstergeleri Sayısı (3 adet)",
        )
        for name, text in zip(FEATURE_NAMES, expected):
            self.assertEqual(format_feature(name, REQUEST[name]), text)
        self.assertEqual(format_feature("DeliveryDelayTrend_Percentage", -15.2), "Gecikme Trendi (-%15.2)")

    def test_settings_and_secret_masking(self):
        config = Settings(_env_file=None)
        self.assertEqual(config.rabbitmq_request_queue, "supplyguard.risk.requests")
        config = Settings(rabbitmq_url="amqp://user:secret@localhost/", _env_file=None)
        self.assertNotIn("secret", repr(config))
        with self.assertRaises(ValidationError):
            Settings(rabbitmq_request_queue="same", rabbitmq_response_queue="same", _env_file=None)

    def test_shap_output_versions(self):
        positive = np.arange(6, dtype=float).reshape(1, 6)
        for raw in ([-positive, positive], positive, np.stack([-positive, positive], axis=-1)):
            np.testing.assert_array_equal(risk_shap_values(raw, 1), positive[0])
        np.testing.assert_array_equal(risk_shap_values(positive, 0), -positive[0])
        for raw in (np.zeros((6, 1)), np.zeros((1, 5)), np.full((1, 6), np.nan)):
            with self.assertRaises(ValueError):
                risk_shap_values(raw, 1)

    def test_shap_base_value_versions(self):
        self.assertEqual(risk_base_value(0.7, 1), 0.7)
        self.assertEqual(risk_base_value(np.array([0.7]), 0), -0.7)
        self.assertEqual(risk_base_value([-0.7, 0.7], 1), 0.7)
        self.assertEqual(risk_base_value([-0.7, 0.7], 0), -0.7)
        for expected in ([1.0, 2.0, 3.0], float("nan")):
            with self.subTest(expected=expected), self.assertRaises(ValueError):
                risk_base_value(expected, 1)

    def test_contributions_are_optional_and_strict_on_the_wire(self):
        base = {"CorrelationId": "c", "SupplierId": "s", "ConfidenceScore": 0.5,
                "ExplanationText": "x", "IsSuccessful": True}
        self.assertIsNone(RiskAnalysisResponse(**base).Contributions)
        with self.assertRaises(ValidationError):
            RiskAnalysisResponse(**base, Contributions=[{"Feature": "f", "Contribution": 1.0, "extra": 1}])
        with self.assertRaises(ValidationError):
            RiskAnalysisResponse(**base, Contributions=[{"Feature": "f", "Contribution": float("inf")}])

    def test_component_credentials_are_url_encoded_and_explicit_url_wins(self):
        from aio_pika.connection import make_url
        config = Settings(
            rabbitmq_host="rabbitmq", rabbitmq_password="p@ss:/word",
            rabbitmq_vhost="supplyguard", _env_file=None,
        )
        parsed = make_url(config.rabbitmq_url.get_secret_value())
        self.assertEqual(parsed.password, "p@ss:/word")
        self.assertEqual(parsed.host, "rabbitmq")
        explicit = Settings(
            rabbitmq_url="amqp://guest:guest@other/", rabbitmq_host="ignored", _env_file=None,
        )
        self.assertEqual(make_url(explicit.rabbitmq_url.get_secret_value()).host, "other")


class ServiceTests(unittest.IsolatedAsyncioTestCase):
    async def test_probability_ranking_and_exact_explanation(self):
        service = engine()
        response = await service.analyze(RiskAnalysisRequest(**REQUEST))
        self.assertAlmostEqual(response.ConfidenceScore, 0.996)
        self.assertEqual(response.ExplanationText,
            "Tedarikçide >%99 (Yüksek Kesinlik) oranında risk tespit edilmiştir. "
            "Riski tetikleyen ana faktörler: Ortalama Teslimat Gecikmesi (2.4 gün), "
            "Kalite Hata Oranı (%0.4). Öte yandan, Zamanında Teslimat Oranı (%95.0) "
            "durumu riski düşürme yönünde etki yapmaktadır.")
        self.assertIsNone(response.ErrorMessage)
        self.assertEqual(response.BaseValue, 0.3)
        self.assertEqual([(c.Feature, c.Contribution) for c in response.Contributions],
                         list(zip(FEATURE_NAMES, [0.6, -0.5, 0.4, 0.2, -0.1, 0.0])))
        frame = service.classifier.predict_proba.call_args.args[0]
        self.assertEqual(list(frame.columns), list(FEATURE_NAMES))
        self.assertEqual(frame.shape, (1, 6))

    async def test_exact_99_percent_and_absent_factors(self):
        for probability, prefix in ((0.99, "%99.0"), (0.987, "%98.7")):
            response = await engine(probability, [[0.0] * 6]).analyze(RiskAnalysisRequest(**REQUEST))
            self.assertIn(prefix + " oranında", response.ExplanationText)
            self.assertIn("pozitif etkili faktör bulunmamıştır", response.ExplanationText)
            self.assertIn("riski düşürme yönünde etki yapan bir faktör bulunmamıştır", response.ExplanationText)

    async def test_invalid_probabilities_fail(self):
        for probabilities in ([[0.2, 0.9]], [[0.1, np.nan]], [[-0.1, 1.1]], [0.8]):
            service = engine()
            service.classifier.predict_proba.return_value = probabilities
            with self.subTest(probabilities=probabilities), self.assertRaises(ValueError):
                await service.analyze(RiskAnalysisRequest(**REQUEST))

    async def test_inference_runs_off_event_loop(self):
        import threading
        service = engine()
        loop_thread = threading.get_ident()
        original = service._analyze
        def analyze(request):
            self.assertNotEqual(threading.get_ident(), loop_thread)
            return original(request)
        with patch.object(service, "_analyze", side_effect=analyze):
            await service.analyze(RiskAnalysisRequest(**REQUEST))

    async def test_public_mlflow_raw_model_and_startup_validation(self):
        service = engine()
        model = SimpleNamespace(get_raw_model=Mock(return_value=service.classifier))
        with patch.object(services.mlflow.pyfunc, "load_model", return_value=model) as load, \
             patch.object(services.shap, "TreeExplainer", return_value=service.explainer), \
             patch.object(services.mlflow, "set_tracking_uri") as tracking, \
             patch.object(services.mlflow, "set_registry_uri"):
            result = RiskAnalysisService.load("http://mlflow:5000")
        self.assertIs(result.model, model)
        load.assert_called_once_with("models:/SupplyGuard_JIT_Model/latest")
        tracking.assert_called_once_with("http://mlflow:5000")
        service.classifier.feature_name_ = list(reversed(FEATURE_NAMES))
        with self.assertRaises(ValueError):
            RiskAnalysisService(model, service.classifier, service.explainer)

    async def test_real_lightgbm_and_shap_match_raw_score(self):
        import pandas as pd
        random = np.random.default_rng(42)
        frame = pd.DataFrame(random.normal(size=(256, 6)), columns=list(FEATURE_NAMES))
        labels = (frame.iloc[:, 0] + frame.iloc[:, 2] > 0).astype(int)
        classifier = LGBMClassifier(n_estimators=12, num_leaves=7, verbosity=-1, n_jobs=1)
        classifier.fit(frame, labels)
        explainer = shap.TreeExplainer(classifier, feature_perturbation="tree_path_dependent")
        service = RiskAnalysisService(object(), classifier, explainer)
        request = RiskAnalysisRequest(**REQUEST)
        response = await service.analyze(request)
        row = pd.DataFrame([{name: REQUEST[name] for name in FEATURE_NAMES}])
        expected = float(classifier.predict_proba(row)[0, 1])
        self.assertAlmostEqual(response.ConfidenceScore, expected)
        contributions = risk_shap_values(explainer.shap_values(row), 1)
        raw = classifier.predict(row, raw_score=True)[0]
        self.assertAlmostEqual(float(np.asarray(explainer.expected_value).reshape(-1)[-1]) + contributions.sum(), raw, places=5)
        # The wire response carries the same decomposition (rounded to 6 decimals).
        wire_total = response.BaseValue + sum(c.Contribution for c in response.Contributions)
        self.assertAlmostEqual(wire_total, raw, places=4)


class WorkerTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.service = engine()
        self.worker = rabbitmq.RabbitMQWorker(Settings(_env_file=None), self.service)
        self.exchange = SimpleNamespace(publish=AsyncMock(return_value=object()))

    async def test_success_publishes_before_ack(self):
        incoming = Incoming()
        async def publish(*args, **kwargs):
            self.assertEqual(incoming.actions, [])
            return object()
        self.exchange.publish.side_effect = publish
        await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("ack",)])
        args = self.exchange.publish.call_args
        self.assertEqual(args.kwargs["routing_key"], "supplyguard.risk.responses")
        self.assertTrue(args.kwargs["mandatory"])
        response = RiskAnalysisResponse.model_validate_json(args.args[0].body)
        self.assertTrue(response.IsSuccessful)
        self.assertEqual(response.CorrelationId, REQUEST["CorrelationId"])

    async def test_malformed_requests_get_failure_response(self):
        for body in (b"{", b"[]", b"null", b"123", b"\xff",
                     json.dumps({"CorrelationId": "c", "SupplierId": "s"}).encode()):
            with self.subTest(body=body):
                incoming = Incoming(body=body)
                await self.worker.handle_message(incoming, self.exchange)
                response = RiskAnalysisResponse.model_validate_json(self.exchange.publish.call_args.args[0].body)
                self.assertFalse(response.IsSuccessful)
                self.assertTrue(response.ErrorMessage)
                self.assertIsNone(response.Contributions)
                self.assertIsNone(response.BaseValue)
                self.assertEqual(response.CorrelationId, "c" if b"CorrelationId" in body else "amqp-fallback")
                self.assertEqual(incoming.actions, [("ack",)])

    async def test_inference_failure_and_next_message_continue(self):
        with patch.object(self.service, "analyze", side_effect=RuntimeError("failure-detail")):
            first = Incoming()
            await self.worker.handle_message(first, self.exchange)
        response = RiskAnalysisResponse.model_validate_json(self.exchange.publish.call_args.args[0].body)
        self.assertFalse(response.IsSuccessful)
        self.assertNotIn("failure-detail", response.ErrorMessage)
        await self.worker.handle_message(Incoming(), self.exchange)
        self.assertTrue(json.loads(self.exchange.publish.call_args.args[0].body)["IsSuccessful"])

    async def test_publication_failure_requeues(self):
        self.exchange.publish.side_effect = TimeoutError
        incoming = Incoming()
        with self.assertRaises(TimeoutError):
            await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("reject", True)])

    async def test_long_correlation_remains_in_json_without_oversized_amqp_header(self):
        correlation = "ç" * 200
        incoming = Incoming(payload={**REQUEST, "CorrelationId": correlation})
        await self.worker.handle_message(incoming, self.exchange)
        message = self.exchange.publish.call_args.args[0]
        self.assertIsNone(message.correlation_id)
        self.assertEqual(json.loads(message.body)["CorrelationId"], correlation)

    async def test_absent_publisher_confirm_requeues(self):
        for confirmation in (None, False):
            self.exchange.publish.return_value = confirmation
            incoming = Incoming()
            with self.assertRaises(RuntimeError):
                await self.worker.handle_message(incoming, self.exchange)
            self.assertEqual(incoming.actions, [("reject", True)])

    async def test_shutdown_cancellation_requeues_without_error_response(self):
        incoming = Incoming()
        with patch.object(self.service, "analyze", side_effect=asyncio.CancelledError):
            with self.assertRaises(asyncio.CancelledError):
                await self.worker.handle_message(incoming, self.exchange)
        self.assertEqual(incoming.actions, [("reject", True)])
        self.exchange.publish.assert_not_awaited()

    async def test_start_is_background_and_stop_cancels(self):
        entered = asyncio.Event()
        cancelled = asyncio.Event()
        async def run():
            entered.set()
            try:
                await asyncio.Future()
            finally:
                cancelled.set()
        with patch.object(self.worker, "_run", side_effect=run):
            await self.worker.start()
            await asyncio.wait_for(entered.wait(), timeout=1)
            await self.worker.stop()
        self.assertTrue(cancelled.is_set())

    async def test_lifespan_keeps_models_and_cleans_up(self):
        app = FastAPI()
        worker = SimpleNamespace(start=AsyncMock(), stop=AsyncMock())
        with patch.object(main.RiskAnalysisService, "load", return_value=self.service), \
             patch.object(main, "RabbitMQWorker", return_value=worker):
            async with main.lifespan(app):
                self.assertIs(app.state.model, self.service.model)
                self.assertIs(app.state.explainer, self.service.explainer)
                worker.start.assert_awaited_once()
        worker.stop.assert_awaited_once()
        self.assertIsNone(app.state.worker)
        self.assertFalse(self.service.is_ready)

    async def test_health_checks_model_and_broker(self):
        app = FastAPI()
        request = Request({"type": "http", "app": app})
        for broker, model in ((False, False), (False, True), (True, False), (True, True)):
            app.state.worker = SimpleNamespace(is_ready=broker, inference_ready=model)
            response = await main.health(request)
            self.assertEqual(response.status_code, 200 if broker and model else 503)

    async def test_connection_topology_consumption_and_cleanup(self):
        published = asyncio.Event()
        incoming = Incoming()
        async def publish(*args, **kwargs):
            published.set()
            return object()
        self.exchange.publish.side_effect = publish
        async def messages():
            yield incoming
            await asyncio.Future()
        @asynccontextmanager
        async def iterator():
            yield messages()
        queue = SimpleNamespace(iterator=iterator)
        channel = SimpleNamespace(
            set_qos=AsyncMock(), declare_queue=AsyncMock(return_value=queue),
            default_exchange=self.exchange,
        )
        closed = asyncio.Event()
        class Connection:
            is_closed = False
            connected = asyncio.Event()
            async def channel(self, **kwargs):
                self.channel_options = kwargs
                return channel
            async def __aenter__(self):
                return self
            async def __aexit__(self, *args):
                closed.set()
        connection = Connection()
        connection.connected.set()
        with patch.object(rabbitmq.aio_pika, "connect_robust", new=AsyncMock(return_value=connection)):
            await self.worker.start()
            await asyncio.wait_for(published.wait(), timeout=2)
            self.assertTrue(self.worker.is_ready)
            await self.worker.stop()
        self.assertTrue(closed.is_set())
        self.assertFalse(self.worker.is_ready)
        self.assertEqual(connection.channel_options, {"publisher_confirms": True, "on_return_raises": True})
        self.assertEqual(
            [call.args[0] for call in channel.declare_queue.await_args_list],
            ["supplyguard.risk.responses", "supplyguard.risk.requests"],
        )
        channel.set_qos.assert_awaited_once_with(prefetch_count=1)
        self.assertEqual(incoming.actions, [("ack",)])

    async def test_connection_failure_retries_and_cancellation_propagates(self):
        with patch.object(rabbitmq.aio_pika, "connect_robust", new=AsyncMock(side_effect=ConnectionError)), \
             patch.object(rabbitmq.asyncio, "sleep", new=AsyncMock(side_effect=asyncio.CancelledError)) as sleep:
            with self.assertRaises(asyncio.CancelledError):
                await self.worker._run()
        sleep.assert_awaited_once_with(self.worker.settings.rabbitmq_retry_seconds)


if __name__ == "__main__":
    unittest.main()
