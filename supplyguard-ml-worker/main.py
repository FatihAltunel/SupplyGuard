"""FastAPI owns the model, explainer, and RabbitMQ background task."""
import asyncio
import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from config import Settings
from rabbitmq import RabbitMQWorker
from services import RiskAnalysisService

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = Settings()
    service = await asyncio.to_thread(RiskAnalysisService.load, settings.mlflow_tracking_uri)
    worker = RabbitMQWorker(settings, service)
    app.state.model = service.model
    app.state.classifier = service.classifier
    app.state.explainer = service.explainer
    app.state.service = service
    app.state.worker = worker
    try:
        await worker.start()
        yield
    finally:
        await worker.stop()
        service.is_ready = False
        for name in ("worker", "service", "model", "classifier", "explainer"):
            setattr(app.state, name, None)


app = FastAPI(title="SupplyGuard ML Worker", lifespan=lifespan)


@app.get("/health")
async def health(request: Request) -> JSONResponse:
    worker = getattr(request.app.state, "worker", None)
    rabbitmq_ready = worker is not None and worker.is_ready
    inference_ready = worker is not None and worker.inference_ready
    healthy = rabbitmq_ready and inference_ready
    return JSONResponse(
        status_code=200 if healthy else 503,
        content={
            "status": "healthy" if healthy else "unhealthy",
            "rabbitmq": "ready" if rabbitmq_ready else "unavailable",
            "inference": "ready" if inference_ready else "unavailable",
        },
    )
