"""Model kalite kapıları: bir model sürümünün champion olabilmesi için geçmesi gereken testler.

Bu modül model kütüphanesinden bağımsızdır: `predict_proba(frame) -> (n, 2)` sunan her
nesneyle çalışır. Böylece aynı kontroller eğitimde (train.py), terfide (promote.py)
ve birim testlerde kullanılır.

Kontrol aileleri:
  1. Sözleşme   : worker'ın başlangıçta yaptığı doğrulamaların aynısı (özellik adı/sırası, sınıflar).
  2. İstatistik : ayrıştırma (AUC, PR-AUC) ve kalibrasyon (Brier, ECE, uç tahmin oranı).
  3. Davranış   : monotonluk ve iş birimiyle birlikte yazılan "golden case" senaryoları.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np
import pandas as pd
from sklearn.metrics import average_precision_score, brier_score_loss, roc_auc_score

from data_generator import FEATURE_NAMES, MONOTONE_DIRECTIONS


# --------------------------------------------------------------------------- #
# Golden case'ler: iş birimiyle birlikte yazılan kabul senaryoları.
# Aralıklar modelin çıktısından DEĞİL, iş beklentisinden gelir. Model bunlara
# uymuyorsa ya model ya da varsayım yanlıştır; ikisi de terfiden önce konuşulmalıdır.
# --------------------------------------------------------------------------- #
GOLDEN_CASES: list[dict[str, Any]] = [
    {"name": "İdeal tedarikçi", "min": 0.00, "max": 0.10, "features": {
        "averageDeliveryDelayDays": 0.1, "onTimeDeliveryRate": 0.99, "qualityDefectRate": 0.0008,
        "DeliveryDelayTrend_Percentage": -5, "FinancialStabilityScore": 90, "ActiveRiskIndicatorsCount": 0}},
    {"name": "Tipik kabul edilebilir tedarikçi", "min": 0.00, "max": 0.15, "features": {
        "averageDeliveryDelayDays": 0.5, "onTimeDeliveryRate": 0.92, "qualityDefectRate": 0.002,
        "DeliveryDelayTrend_Percentage": 2, "FinancialStabilityScore": 72, "ActiveRiskIndicatorsCount": 1}},
    {"name": "İzlenmesi gereken tedarikçi", "min": 0.15, "max": 0.55, "features": {
        "averageDeliveryDelayDays": 1.3, "onTimeDeliveryRate": 0.78, "qualityDefectRate": 0.005,
        "DeliveryDelayTrend_Percentage": 15, "FinancialStabilityScore": 60, "ActiveRiskIndicatorsCount": 2}},
    {"name": "Lojistik kriz", "min": 0.60, "max": 1.00, "features": {
        "averageDeliveryDelayDays": 3.0, "onTimeDeliveryRate": 0.50, "qualityDefectRate": 0.003,
        "DeliveryDelayTrend_Percentage": 35, "FinancialStabilityScore": 60, "ActiveRiskIndicatorsCount": 2}},
    {"name": "Kalite krizi", "min": 0.25, "max": 0.75, "features": {
        "averageDeliveryDelayDays": 0.8, "onTimeDeliveryRate": 0.86, "qualityDefectRate": 0.02,
        "DeliveryDelayTrend_Percentage": 8, "FinancialStabilityScore": 55, "ActiveRiskIndicatorsCount": 3}},
    {"name": "Finansal çöküş sinyali", "min": 0.25, "max": 0.75, "features": {
        "averageDeliveryDelayDays": 0.7, "onTimeDeliveryRate": 0.88, "qualityDefectRate": 0.003,
        "DeliveryDelayTrend_Percentage": 5, "FinancialStabilityScore": 25, "ActiveRiskIndicatorsCount": 3}},
]
# Risk sırası: soldaki her zaman sağdakinden daha az riskli görünmeli.
GOLDEN_ORDER = ["İdeal tedarikçi", "Tipik kabul edilebilir tedarikçi",
                "İzlenmesi gereken tedarikçi", "Lojistik kriz"]


@dataclass(frozen=True)
class Gates:
    """Terfi eşikleri. Değiştirmek bir iş kararıdır; MLflow'a parametre olarak loglanır."""
    min_auc: float = 0.80
    max_ece: float = 0.05
    max_extreme_share: float = 0.05      # tahminlerin en fazla %5'i <%0.5 veya >%99 olabilir
    max_mean_pred_gap: float = 0.03      # ortalama tahmin ile gerçek oran farkı
    max_monotonic_violation_rate: float = 0.0
    max_auc_drop_vs_champion: float = 0.01
    max_brier_increase_vs_champion: float = 0.005


@dataclass
class CheckReport:
    metrics: dict[str, float] = field(default_factory=dict)
    golden: list[dict[str, Any]] = field(default_factory=list)
    failures: list[str] = field(default_factory=list)

    @property
    def passed(self) -> bool:
        return not self.failures


def risk_proba(model: Any, frame: pd.DataFrame) -> np.ndarray:
    proba = np.asarray(model.predict_proba(frame[list(FEATURE_NAMES)]), dtype=float)
    if proba.ndim != 2 or proba.shape[1] != 2:
        raise ValueError("predict_proba (n, 2) döndürmelidir.")
    return proba[:, list(model.classes_).index(1)]


def expected_calibration_error(y: np.ndarray, p: np.ndarray, bins: int = 10) -> float:
    idx = np.clip((p * bins).astype(int), 0, bins - 1)
    total = 0.0
    for b in range(bins):
        mask = idx == b
        if mask.any():
            total += abs(p[mask].mean() - y[mask].mean()) * mask.mean()
    return float(total)


def contract_check(model: Any) -> list[str]:
    """Worker'ın services.py başlangıç doğrulamasıyla aynı kurallar."""
    failures = []
    classes = list(getattr(model, "classes_", []))
    if sorted(classes) != [0, 1]:
        failures.append(f"Sınıflar [0, 1] olmalı, bulunan: {classes}")
    names = getattr(model, "feature_name_", None)
    if names is not None and list(names) != list(FEATURE_NAMES):
        failures.append(f"Özellik adı/sırası sözleşmeyle uyuşmuyor: {list(names)}")
    if not callable(getattr(model, "predict_proba", None)):
        failures.append("predict_proba yok")
    return failures


def statistical_metrics(model: Any, x: pd.DataFrame, y: np.ndarray) -> dict[str, float]:
    p = risk_proba(model, x)
    return {
        "auc": float(roc_auc_score(y, p)),
        "pr_auc": float(average_precision_score(y, p)),
        "brier": float(brier_score_loss(y, p)),
        "ece": expected_calibration_error(y, p),
        "mean_prediction": float(p.mean()),
        "positive_rate": float(y.mean()),
        "extreme_share": float(np.mean((p < 0.005) | (p > 0.99))),
        "mid_range_share": float(np.mean((p > 0.10) & (p < 0.90))),
    }


def monotonicity_violation_rate(model: Any, reference: pd.DataFrame,
                                n_rows: int = 200, grid_size: int = 25, seed: int = 0) -> dict[str, float]:
    """Her özellik için: diğerleri sabitken değeri artır, risk beklenen yönde mi hareket ediyor?

    Örneğin gecikme 1 günden 1.5 güne çıkınca risk düşüyorsa, iş birimi modele güvenmez;
    SHAP açıklaması da "gecikme riski azalttı" gibi saçma bir cümle üretir.
    """
    rows = reference[list(FEATURE_NAMES)].sample(min(n_rows, len(reference)), random_state=seed)
    rates = {}
    for feature, direction in MONOTONE_DIRECTIONS.items():
        grid = np.unique(np.quantile(reference[feature], np.linspace(0.01, 0.99, grid_size)))
        batch = rows.loc[rows.index.repeat(len(grid))].copy()
        batch[feature] = np.tile(grid, len(rows))
        p = risk_proba(model, batch).reshape(len(rows), len(grid))
        diffs = np.diff(p, axis=1) * direction
        rates[feature] = float(np.mean(diffs < -1e-9))
    return rates


def golden_case_results(model: Any) -> list[dict[str, Any]]:
    frame = pd.DataFrame([c["features"] for c in GOLDEN_CASES], columns=list(FEATURE_NAMES))
    p = risk_proba(model, frame)
    return [{"name": c["name"], "min": c["min"], "max": c["max"], "risk": round(float(v), 4),
             "passed": bool(c["min"] <= v <= c["max"])} for c, v in zip(GOLDEN_CASES, p)]


def run_checks(model: Any, x: pd.DataFrame, y: np.ndarray, gates: Gates = Gates()) -> CheckReport:
    report = CheckReport()
    report.failures += contract_check(model)

    report.metrics.update(statistical_metrics(model, x, y))
    m = report.metrics
    if m["auc"] < gates.min_auc:
        report.failures.append(f"AUC {m['auc']:.3f} < {gates.min_auc}")
    if m["ece"] > gates.max_ece:
        report.failures.append(f"ECE {m['ece']:.3f} > {gates.max_ece} (olasılıklar kalibre değil)")
    if m["extreme_share"] > gates.max_extreme_share:
        report.failures.append(f"Uç tahmin oranı %{m['extreme_share'] * 100:.1f} (aşırı özgüven)")
    if abs(m["mean_prediction"] - m["positive_rate"]) > gates.max_mean_pred_gap:
        report.failures.append(
            f"Ortalama tahmin {m['mean_prediction']:.3f}, gerçek oran {m['positive_rate']:.3f}")

    for feature, rate in monotonicity_violation_rate(model, x).items():
        report.metrics[f"monotonic_violation_{feature}"] = rate
        if rate > gates.max_monotonic_violation_rate:
            report.failures.append(f"Monotonluk ihlali: {feature} (%{rate * 100:.1f})")

    report.golden = golden_case_results(model)
    for g in report.golden:
        if not g["passed"]:
            report.failures.append(f"Golden case: {g['name']} risk={g['risk']:.2f}, beklenen [{g['min']}, {g['max']}]")
    by_name = {g["name"]: g["risk"] for g in report.golden}
    ordered = [by_name[n] for n in GOLDEN_ORDER]
    if any(a >= b for a, b in zip(ordered, ordered[1:])):
        report.failures.append(f"Golden sıralama bozuk: {dict(zip(GOLDEN_ORDER, ordered))}")
    report.metrics["golden_pass_rate"] = float(np.mean([g["passed"] for g in report.golden]))
    return report


def compare_to_champion(challenger: CheckReport, champion: CheckReport, gates: Gates = Gates()) -> list[str]:
    """Challenger, aynı referans veride champion'dan anlamlı ölçüde kötü olmamalı."""
    failures = []
    auc_drop = champion.metrics["auc"] - challenger.metrics["auc"]
    if auc_drop > gates.max_auc_drop_vs_champion:
        failures.append(f"AUC champion'a göre {auc_drop:.3f} düştü")
    brier_up = challenger.metrics["brier"] - champion.metrics["brier"]
    if brier_up > gates.max_brier_increase_vs_champion:
        failures.append(f"Brier champion'a göre {brier_up:.4f} arttı")
    return failures
