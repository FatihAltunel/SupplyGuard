"""FastAPI application setup, component lifecycle, and readiness endpoint."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import cast

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from config import Settings
from inference import MockInference
from rabbitmq import RabbitMQWorker


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    inference = MockInference()
    worker = RabbitMQWorker(Settings(), inference)
    app.state.worker = worker
    try:
        await inference.initialize()
        # Includes capped exponential startup retries while RabbitMQ is unavailable.
        await worker.start()
        yield
    finally:
        try:
            await worker.stop()
        finally:
            await inference.close()
            app.state.worker = None


app = FastAPI(title="SupplyGuard Dummy ML Worker", lifespan=lifespan)


@app.get("/health")
async def health(request: Request) -> JSONResponse:
    worker = cast(RabbitMQWorker | None, getattr(request.app.state, "worker", None))
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
