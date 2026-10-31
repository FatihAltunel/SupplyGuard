"""Validated environment settings; credentials are never included in repr/errors."""

from typing import Annotated, Self

from pydantic import Field, SecretStr, StringConstraints, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

Name = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=255)]
PositiveSeconds = Annotated[float, Field(gt=0, allow_inf_nan=False)]


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="RABBITMQ_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        hide_input_in_errors=True,
        frozen=True,
    )

    host: Name = "localhost"
    port: Annotated[int, Field(ge=1, le=65535)] = 5672
    user: Name = "supplyguard"
    password: SecretStr = Field(min_length=1, repr=False)
    vhost: Name = "supplyguard"
    use_tls: bool = False
    exchange: Name = "supplyguard.events"
    request_queue: Name = "risk-analysis.requested.queue"
    completed_queue: Name = "risk-analysis.completed.queue"
    error_exchange: Name = "supplyguard.ml.errors"
    error_queue: Name = "risk-analysis.invalid.queue"
    prefetch_count: Annotated[int, Field(ge=1, le=65535)] = 1
    connection_timeout_seconds: PositiveSeconds = 10
    publish_timeout_seconds: PositiveSeconds = 10
    retry_initial_seconds: PositiveSeconds = 1
    retry_max_seconds: PositiveSeconds = 30
    dedup_max_entries: Annotated[int, Field(ge=1)] = 10000
    dedup_ttl_seconds: PositiveSeconds = 86400

    @model_validator(mode="after")
    def validate_topology(self) -> Self:
        if self.retry_max_seconds < self.retry_initial_seconds:
            raise ValueError("Retry maximum must be at least the initial delay.")
        if len({self.request_queue, self.completed_queue, self.error_queue}) != 3:
            raise ValueError("Request, completion, and error queues must be distinct.")
        if self.exchange == self.error_exchange:
            raise ValueError("Event and error exchanges must be distinct.")
        return self
