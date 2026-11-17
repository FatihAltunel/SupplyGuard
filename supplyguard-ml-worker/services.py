"""MLflow loading, risk prediction, and Turkish SHAP explanations."""
import asyncio
from typing import Any

import mlflow
import mlflow.pyfunc
import numpy as np
import pandas as pd
import shap

from schemas import FEATURE_NAMES, RiskAnalysisRequest, RiskAnalysisResponse

MODEL_URI = "models:/SupplyGuard_JIT_Model/latest"
FEATURE_LABELS = {
    "averageDeliveryDelayDays": "Ortalama Teslimat Gecikmesi",
    "onTimeDeliveryRate": "Zamanında Teslimat Oranı",
    "qualityDefectRate": "Kalite Hata Oranı",
    "DeliveryDelayTrend_Percentage": "Gecikme Trendi",
    "FinancialStabilityScore": "Finansal İstikrar Skoru",
    "ActiveRiskIndicatorsCount": "Aktif Risk Göstergeleri Sayısı",
}


def format_feature(name: str, value: float) -> str:
    # Delivery/defect rates use 0..1; trend is already in percentage points.
    if name == "averageDeliveryDelayDays":
        formatted = f"{value:.1f} gün"
    elif name in {"onTimeDeliveryRate", "qualityDefectRate"}:
        formatted = f"%{value * 100:.1f}"
    elif name == "DeliveryDelayTrend_Percentage":
        sign = "+" if value >= 0 else "-"
        formatted = f"{sign}%{abs(value):.1f}"
    elif name == "ActiveRiskIndicatorsCount":
        formatted = f"{value:g} adet"
    else:
        formatted = f"{value:.1f}"
    return f"{FEATURE_LABELS[name]} ({formatted})"


def risk_shap_values(raw: Any, risk_index: int) -> np.ndarray:
    """Normalize old list and new ndarray SHAP outputs."""
    single_output = False
    if isinstance(raw, list):
        if len(raw) != 2:
            raise ValueError("SHAP must describe a binary classifier.")
        values = np.asarray(raw[risk_index], dtype=float)
    else:
        values = np.asarray(raw, dtype=float)
        if values.shape == (1, len(FEATURE_NAMES), 2):
            values = values[:, :, risk_index]
        else:
            single_output = True
    if values.shape != (1, len(FEATURE_NAMES)) or not np.isfinite(values).all():
        raise ValueError("Unexpected or non-finite SHAP output.")
    # LightGBM's single raw output describes classes_[1].
    return -values[0] if single_output and risk_index == 0 else values[0]


class RiskAnalysisService:
    def __init__(self, model: Any, classifier: Any, explainer: Any) -> None:
        self.model = model
        self.classifier = classifier
        self.explainer = explainer
        self.is_ready = True
        self._lock = asyncio.Lock()
        classes = list(classifier.classes_)
        if len(classes) != 2 or classes.count(1) != 1:
            raise ValueError("A binary classifier with risk label 1 is required.")
        self.risk_index = classes.index(1)
        if list(classifier.feature_name_) != list(FEATURE_NAMES):
            raise ValueError("Model feature names/order do not match the request schema.")

    @classmethod
    def load(cls, tracking_uri: str) -> "RiskAnalysisService":
        mlflow.set_tracking_uri(tracking_uri)
        mlflow.set_registry_uri(tracking_uri)
        model = mlflow.pyfunc.load_model(MODEL_URI)
        # Public MLflow 3.18 API; uses the same loaded artifact/version.
        classifier = model.get_raw_model()
        if not callable(getattr(classifier, "predict_proba", None)):
            raise TypeError("Register an LGBMClassifier exposing predict_proba().")
        explainer = shap.TreeExplainer(
            classifier, model_output="raw", feature_perturbation="tree_path_dependent"
        )
        return cls(model, classifier, explainer)

    async def analyze(self, request: RiskAnalysisRequest) -> RiskAnalysisResponse:
        async with self._lock:
            return await asyncio.to_thread(self._analyze, request)

    def _analyze(self, request: RiskAnalysisRequest) -> RiskAnalysisResponse:
        frame = pd.DataFrame(
            [{name: getattr(request, name) for name in FEATURE_NAMES}],
            columns=list(FEATURE_NAMES), dtype=float,
        )
        probabilities = np.asarray(self.classifier.predict_proba(frame), dtype=float)
        if (probabilities.shape != (1, 2) or not np.isfinite(probabilities).all()
                or np.any(probabilities < 0) or np.any(probabilities > 1)
                or not np.isclose(probabilities.sum(), 1.0)):
            raise ValueError("The model returned invalid binary probabilities.")
        confidence = float(probabilities[0, self.risk_index])
        contributions = risk_shap_values(self.explainer.shap_values(frame), self.risk_index)
        positive = sorted(
            (i for i, value in enumerate(contributions) if value > 0),
            key=lambda i: contributions[i], reverse=True,
        )[:2]
        negative = sorted(
            (i for i, value in enumerate(contributions) if value < 0),
            key=lambda i: contributions[i],
        )[:1]

        def describe(indices: list[int]) -> str:
            return ", ".join(
                format_feature(FEATURE_NAMES[i], float(frame.iloc[0, i])) for i in indices
            )

        risk_text = ">%99 (Yüksek Kesinlik)" if confidence > 0.99 else f"%{confidence * 100:.1f}"
        positive_text = describe(positive) if positive else "pozitif etkili faktör bulunmamıştır"
        explanation = (
            f"Tedarikçide {risk_text} oranında risk tespit edilmiştir. "
            f"Riski tetikleyen ana faktörler: {positive_text}. "
        )
        if negative:
            explanation += (
                f"Öte yandan, {describe(negative)} durumu riski düşürme yönünde etki yapmaktadır."
            )
        else:
            explanation += "Öte yandan, riski düşürme yönünde etki yapan bir faktör bulunmamıştır."
        return RiskAnalysisResponse(
            CorrelationId=request.CorrelationId, SupplierId=request.SupplierId,
            ConfidenceScore=confidence, ExplanationText=explanation, IsSuccessful=True,
        )
