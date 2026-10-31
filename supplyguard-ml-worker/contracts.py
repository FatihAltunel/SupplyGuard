"""Strict v1 JSON contracts; independent of transport and inference."""

import math
from datetime import datetime
from typing import Annotated, Final, Literal, Self
from uuid import UUID

from pydantic import BaseModel, BeforeValidator, ConfigDict, Field, PlainSerializer, model_validator
from pydantic.alias_generators import to_camel

from validators import format_timestamp, parse_timestamp, parse_uuid

REQUEST_TYPE: Final = "risk-analysis.requested.v1"
COMPLETED_TYPE: Final = "risk-analysis.completed.v1"


EventId = Annotated[UUID, BeforeValidator(parse_uuid)]
UtcTimestamp = Annotated[
    datetime,
    BeforeValidator(parse_timestamp),
    PlainSerializer(format_timestamp, return_type=str, when_used="json"),
]
Number = Annotated[float, Field(strict=True, allow_inf_nan=False)]
FeatureName = Annotated[str, Field(min_length=1, pattern=r"\S")]


class Contract(BaseModel):
    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        serialize_by_alias=True,
        extra="forbid",
        strict=True,
        allow_inf_nan=False,
    )


class Envelope(Contract):
    message_id: EventId
    correlation_id: EventId
    occurred_at_utc: UtcTimestamp
    supplier_id: EventId


class AnalysisInput(Contract):
    feature_schema_version: Literal[1]
    observed_at_utc: UtcTimestamp
    features: Annotated[dict[FeatureName, Number | None], Field(min_length=1)]

    @model_validator(mode="before")
    @classmethod
    def require_integer_version(cls, value: object) -> object:
        # Literal[1] alone can accept True or 1.0 through Python equality.
        if isinstance(value, dict):
            version = value.get("featureSchemaVersion", value.get("feature_schema_version"))
            if type(version) is not int:
                raise ValueError("featureSchemaVersion must be an integer.")
        return value


class RiskAnalysisRequestedEvent(Envelope):
    message_type: Literal["risk-analysis.requested.v1"]
    input: AnalysisInput


class ModelInfo(Contract):
    name: Annotated[str, Field(min_length=1, max_length=200, pattern=r"\S")]
    version: Annotated[str, Field(min_length=1, max_length=100, pattern=r"\S")]
    is_mock: bool


class Execution(Contract):
    started_at_utc: UtcTimestamp
    completed_at_utc: UtcTimestamp
    latency_ms: Annotated[int, Field(ge=0, le=2_147_483_647)]

    @model_validator(mode="after")
    def validate_order(self) -> Self:
        if self.completed_at_utc < self.started_at_utc:
            raise ValueError("Completion cannot precede execution start.")
        return self


class Contribution(Contract):
    feature: FeatureName
    contribution: Number


class Explanation(Contract):
    method: Literal["dummy-shap"]
    reason: Annotated[str, Field(min_length=1, max_length=4000, pattern=r"\S")]
    output_scale: Literal["risk-score-0-100"]
    base_value: Number
    contributions: list[Contribution]


class AnalysisResult(Contract):
    risk_score: Annotated[Number, Field(ge=0, le=100, multiple_of=0.01)]
    confidence_score: Annotated[Number, Field(ge=0, le=1, multiple_of=0.0001)]
    explanation: Explanation

    @model_validator(mode="after")
    def validate_additivity(self) -> Self:
        total = self.explanation.base_value + math.fsum(
            item.contribution for item in self.explanation.contributions
        )
        if not math.isclose(total, self.risk_score, abs_tol=0.000001, rel_tol=0):
            raise ValueError("Explanation contributions must reconstruct the risk score.")
        return self


class AnalysisError(Contract):
    code: Annotated[str, Field(min_length=1, max_length=100, pattern=r"\S")]
    message: Annotated[str, Field(min_length=1, max_length=2000, pattern=r"\S")]


class RiskAnalysisCompletedEvent(Envelope):
    message_type: Literal["risk-analysis.completed.v1"]
    request_message_id: EventId
    status: Literal["succeeded", "failed"]
    model: ModelInfo
    execution: Execution
    result: AnalysisResult | None
    error: AnalysisError | None

    @model_validator(mode="after")
    def validate_outcome(self) -> Self:
        if self.status == "succeeded" and (self.result is None or self.error is not None):
            raise ValueError("Success requires a result and an explicit null error.")
        if self.status == "failed" and (self.result is not None or self.error is None):
            raise ValueError("Failure requires an error and an explicit null result.")
        if self.occurred_at_utc != self.execution.completed_at_utc:
            raise ValueError("Event occurrence must equal execution completion.")
        return self
