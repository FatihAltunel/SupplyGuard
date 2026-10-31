"""Phase A inference boundary; replace the dummy computation here in Phase B."""

import asyncio
import logging
from time import perf_counter
from uuid import uuid4

from contracts import (
    COMPLETED_TYPE,
    AnalysisError,
    AnalysisResult,
    Contribution,
    Execution,
    Explanation,
    ModelInfo,
    RiskAnalysisCompletedEvent,
    RiskAnalysisRequestedEvent,
)
from validators import utc_now

logger = logging.getLogger("uvicorn.error")


def dummy_result() -> AnalysisResult:
    return AnalysisResult(
        risk_score=65.0,
        confidence_score=0.9,
        explanation=Explanation(
            method="dummy-shap",
            reason="Simulated elevated delivery risk. This is a fixed dummy result.",
            output_scale="risk-score-0-100",
            base_value=30.0,
            contributions=[
                Contribution(feature="averageDeliveryDelayDays", contribution=25.0),
                Contribution(feature="onTimeDeliveryRate", contribution=10.0),
            ],
        ),
    )


class MockInference:
    def __init__(self) -> None:
        self._ready = False

    @property
    def is_ready(self) -> bool:
        return self._ready

    async def initialize(self) -> None:
        # Phase B will load and validate the model before marking it ready.
        dummy_result()
        self._ready = True

    async def close(self) -> None:
        self._ready = False

    async def analyze(self, request: RiskAnalysisRequestedEvent) -> RiskAnalysisCompletedEvent:
        if not self.is_ready:
            raise RuntimeError("Inference has not been initialized.")
        started_at = utc_now()
        timer = perf_counter()
        result: AnalysisResult | None = None
        error: AnalysisError | None = None
        for attempt in range(3):
            try:
                await asyncio.sleep(2)
                result = dummy_result()
                break
            except Exception as exception:
                # Cancellation propagates; diagnostic messages must not expose inputs.
                logger.warning(
                    "Dummy processing attempt %d failed (%s).",
                    attempt + 1,
                    type(exception).__name__,
                )
                if attempt < 2:
                    await asyncio.sleep(1)
                else:
                    error = AnalysisError(
                        code="DUMMY_PROCESSING_FAILED",
                        message="The dummy worker could not complete the analysis.",
                    )
        completed_at = max(utc_now(), started_at)
        return RiskAnalysisCompletedEvent(
            message_id=uuid4(),
            message_type=COMPLETED_TYPE,
            correlation_id=request.correlation_id,
            occurred_at_utc=completed_at,
            supplier_id=request.supplier_id,
            request_message_id=request.message_id,
            status="succeeded" if error is None else "failed",
            model=ModelInfo(name="supplyguard-dummy", version="0.1.0", is_mock=True),
            execution=Execution(
                started_at_utc=started_at,
                completed_at_utc=completed_at,
                latency_ms=int((perf_counter() - timer) * 1000),
            ),
            result=result,
            error=error,
        )
