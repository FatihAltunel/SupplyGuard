"""SupplyGuard v2 sentetik veri üreticisi.

v1'de etiket (is_high_risk) doğrudan "hangi kümeden üretildi" bilgisiydi; kümeler
birbirinden neredeyse tamamen ayrık olduğu için model her tedarikçiyi %0 ya da %100
olarak görüyordu. v2'de etiket özelliklerden *türetilir*:

    gizli stres faktörleri  ->  gözlenen metrikler  ->  risk logit'i  ->  Bernoulli etiket

Bernoulli örnekleme, gerçek hayattaki indirgenemez belirsizliği temsil eder: aynı
metriklere sahip iki tedarikçiden biri krize girer, diğeri girmez. Model bu yüzden
%100 kesinlik öğrenemez; olasılıklar anlamlı hale gelir.

ÖNEMLİ: Bu veri gerçek değildir. Model, aşağıdaki RISK_WEIGHTS'te yazdığımız iş
varsayımlarını öğrenir. Bu varsayımlar bir domain uzmanıyla (ör. satın alma /
tedarik zinciri kalite ekibi) gözden geçirilmeli ve değiştikçe yeni model sürümü
olarak eğitilmelidir. Sayılar MLflow'a parametre olarak loglanır.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field

import numpy as np
import pandas as pd

FEATURE_NAMES = (
    "averageDeliveryDelayDays", "onTimeDeliveryRate", "qualityDefectRate",
    "DeliveryDelayTrend_Percentage", "FinancialStabilityScore", "ActiveRiskIndicatorsCount",
)
TARGET = "is_high_risk"

# Her özelliğin riske etki yönü. LightGBM monotone_constraints ve kalite kapıları
# bu tabloyu kullanır: +1 = değer arttıkça risk azalamaz, -1 = değer arttıkça risk artamaz.
MONOTONE_DIRECTIONS = {
    "averageDeliveryDelayDays": 1,
    "onTimeDeliveryRate": -1,
    "qualityDefectRate": 1,
    "DeliveryDelayTrend_Percentage": 1,
    "FinancialStabilityScore": -1,
    "ActiveRiskIndicatorsCount": 1,
}


@dataclass(frozen=True)
class GeneratorConfig:
    n_samples: int = 20_000
    seed: int = 42
    # Gizli stres faktörleri arasındaki korelasyon: lojistik sorun yaşayan
    # tedarikçinin finansal durumu da kötüleşme eğiliminde olur.
    stress_correlation: float = 0.35
    # Tedarikçilerin bir kısmı "sıkıntılı" alt popülasyondan gelir. Bu, kuyruk
    # bölgesini (3+ gün gecikme, düşük finans) veride temsil eder; ağaç modelleri
    # eğitim verisinin dışına ekstrapolasyon yapamaz, görmediği bölgede düzleşir.
    distressed_share: float = 0.18
    distressed_shift: float = 1.4
    # Risk logit'i ağırlıkları (iş varsayımları).
    intercept: float = -4.4
    w_delay_days: float = 0.75          # her ek gecikme günü
    w_defect_pct: float = 0.55          # her +%1 hata oranı (0.01)
    w_trend_per_10pct: float = 0.30     # gecikme trendinde her +%10
    w_financial_per_10pts: float = -0.45  # finansal skorda her +10 puan
    w_risk_indicator: float = 0.45      # her aktif risk göstergesi
    w_delay_x_trend: float = 0.25       # kötüleşen gecikme: gecikme ve trend birlikte
    # Ölçülemeyen etkenler (tedarikçinin kendi müşteri kaybı, grev, vb.)
    logit_noise_std: float = 0.5
    # Etiketlerin bir kısmı yanlış kayıtlı (insan hatası, geç kapanan vakalar).
    label_flip_rate: float = 0.02
    extra: dict = field(default_factory=dict)

    def as_params(self) -> dict:
        params = {f"gen_{k}": v for k, v in asdict(self).items() if k != "extra"}
        params.update({f"gen_{k}": v for k, v in self.extra.items()})
        return params


def risk_logit(frame: pd.DataFrame, cfg: GeneratorConfig) -> np.ndarray:
    """Gerçek (gizli) risk fonksiyonu. Her özellikte MONOTONE_DIRECTIONS ile uyumludur."""
    delay = frame["averageDeliveryDelayDays"].to_numpy()
    defect_pct = frame["qualityDefectRate"].to_numpy() * 100
    trend = frame["DeliveryDelayTrend_Percentage"].to_numpy()
    fin = frame["FinancialStabilityScore"].to_numpy()
    indicators = frame["ActiveRiskIndicatorsCount"].to_numpy()
    worsening = np.clip(trend, 0, None) / 10  # sadece kötüleşme etkileşime girer
    return (
        cfg.intercept
        + cfg.w_delay_days * delay
        + cfg.w_defect_pct * defect_pct
        + cfg.w_trend_per_10pct * np.clip(trend, -30, 80) / 10
        + cfg.w_financial_per_10pts * (fin - 70) / 10
        + cfg.w_risk_indicator * indicators
        + cfg.w_delay_x_trend * delay * worsening
    )


def generate(cfg: GeneratorConfig | None = None) -> pd.DataFrame:
    cfg = cfg or GeneratorConfig()
    rng = np.random.default_rng(cfg.seed)
    n = cfg.n_samples

    # 1) Gizli faktörler: lojistik (L), kalite (Q), finans (F) stresi.
    c = cfg.stress_correlation
    cov = np.array([[1, c, c], [c, 1, c / 2], [c, c / 2, 1]])
    L, Q, F = rng.multivariate_normal(np.zeros(3), cov, size=n).T
    distressed = rng.random(n) < cfg.distressed_share
    # Sıkıntılı tedarikçide stres tek bir eksende yoğunlaşır (lojistik, kalite veya finans).
    axis = rng.integers(0, 3, n)
    shift = cfg.distressed_shift * distressed
    L, Q, F = L + shift * (axis == 0) + 0.4 * shift, Q + shift * (axis == 1), F + shift * (axis == 2) + 0.3 * shift

    # 2) Gözlenen metrikler. Dağılımlar sağa çarpık (çoğu tedarikçi iyi, az sayıda çok kötü).
    delay = np.exp(-1.0 + 0.75 * L + rng.normal(0, 0.35, n))                 # gün, medyan ~0.4
    on_time = np.clip(0.99 - 0.16 * delay + rng.normal(0, 0.05, n), 0, 1)    # gecikmeyle ilişkili ama birebir değil
    defect = np.clip(np.exp(-6.2 + 0.8 * Q + rng.normal(0, 0.4, n)), 0, 0.2)  # medyan ~%0.2
    trend = 12 * L + rng.normal(0, 12, n)                                      # yüzde puan
    fin = np.clip(72 - 13 * F + rng.normal(0, 7, n), 0, 100)
    stress = np.maximum.reduce([L, Q, F])
    indicators = np.clip(rng.poisson(np.exp(-0.9 + 0.7 * stress)), 0, 5)

    frame = pd.DataFrame({
        "averageDeliveryDelayDays": delay,
        "onTimeDeliveryRate": on_time,
        "qualityDefectRate": defect,
        "DeliveryDelayTrend_Percentage": trend,
        "FinancialStabilityScore": fin,
        "ActiveRiskIndicatorsCount": indicators.astype(float),
    }, columns=list(FEATURE_NAMES))

    # 3) Etiket: logit + ölçülemeyen gürültü -> Bernoulli.
    logit = risk_logit(frame, cfg) + rng.normal(0, cfg.logit_noise_std, n)
    y = rng.random(n) < 1 / (1 + np.exp(-logit))
    flip = rng.random(n) < cfg.label_flip_rate
    frame[TARGET] = np.where(flip, ~y, y).astype(int)
    return frame


if __name__ == "__main__":
    df = generate()
    print(df.describe().T.round(4))
    print("Pozitif oranı:", round(df[TARGET].mean(), 4))
