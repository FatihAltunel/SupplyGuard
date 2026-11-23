"""JSON contracts shared with .NET."""
from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field

FEATURE_NAMES = (
    "averageDeliveryDelayDays", "onTimeDeliveryRate", "qualityDefectRate",
    "DeliveryDelayTrend_Percentage", "FinancialStabilityScore", "ActiveRiskIndicatorsCount",
)
FiniteFloat = Annotated[float, Field(strict=True, allow_inf_nan=False)]


class RiskAnalysisRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", hide_input_in_errors=True)

    CorrelationId: str = Field(min_length=1)
    SupplierId: str = Field(min_length=1)
    averageDeliveryDelayDays: FiniteFloat
    onTimeDeliveryRate: FiniteFloat
    qualityDefectRate: FiniteFloat
    DeliveryDelayTrend_Percentage: FiniteFloat
    FinancialStabilityScore: FiniteFloat
    ActiveRiskIndicatorsCount: FiniteFloat


class FeatureContribution(BaseModel):
    """One SHAP value for the risk class, in LightGBM raw-score (log-odds) units."""
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)

    Feature: str = Field(min_length=1)
    Contribution: float


class RiskAnalysisResponse(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)

    CorrelationId: str
    SupplierId: str
    ConfidenceScore: float = Field(ge=0, le=1)
    ExplanationText: str
    IsSuccessful: bool
    ErrorMessage: str | None = None
    # Success only: BaseValue + sum(Contributions) reconstructs the risk-class log-odds.
    BaseValue: float | None = None
    Contributions: list[FeatureContribution] | None = None
