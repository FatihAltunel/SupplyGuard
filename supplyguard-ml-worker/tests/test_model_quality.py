"""Eğitim reçetesinin kalite kapılarını geçtiğini, bozuk reçetenin kaldığını doğrular.

MLflow gerektirmez. Veri üreticisi, model parametreleri veya kapılar değiştiğinde
bu test, "hâlâ inandırıcı bir model üretebiliyor muyuz?" sorusunu yanıtlar.
"""
import sys
import unittest
from pathlib import Path

from lightgbm import LGBMClassifier
from sklearn.model_selection import train_test_split

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "training"))
from data_generator import (  # noqa: E402
    FEATURE_NAMES, MONOTONE_DIRECTIONS, TARGET, GeneratorConfig, generate, risk_logit)
from model_checks import golden_case_results, run_checks  # noqa: E402


def fit(params):
    df = generate(GeneratorConfig(n_samples=12_000))
    x, y = df[list(FEATURE_NAMES)], df[TARGET].to_numpy()
    x_train, x_test, y_train, y_test = train_test_split(x, y, test_size=0.25, stratify=y, random_state=0)
    model = LGBMClassifier(n_estimators=300, learning_rate=0.05, num_leaves=15, min_child_samples=50,
                           random_state=42, verbose=-1, n_jobs=1, **params)
    return model.fit(x_train, y_train), x_test, y_test


class ModelQualityTests(unittest.TestCase):
    def test_monotone_unweighted_recipe_passes_gates(self):
        model, x, y = fit({"monotone_constraints": [MONOTONE_DIRECTIONS[f] for f in FEATURE_NAMES],
                           "monotone_constraints_method": "advanced"})
        report = run_checks(model, x, y)
        self.assertTrue(report.passed, report.failures)

    def test_balanced_class_weight_fails_calibration(self):
        # v1 reçetesi: olasılıkları şişirir; kapılar bunu yakalamalı.
        model, x, y = fit({"class_weight": "balanced"})
        report = run_checks(model, x, y)
        self.assertFalse(report.passed)
        self.assertTrue(any("ECE" in f or "Ortalama tahmin" in f for f in report.failures), report.failures)

    def test_generator_ground_truth_agrees_with_golden_cases(self):
        # Golden aralıkları gerçek risk fonksiyonuyla çelişmemeli (varsayım tutarlılığı).
        class Oracle:
            classes_ = [0, 1]

            def predict_proba(self, frame):
                import numpy as np
                p = 1 / (1 + np.exp(-risk_logit(frame, GeneratorConfig())))
                return np.column_stack([1 - p, p])

        failed = [g for g in golden_case_results(Oracle()) if not g["passed"]]
        self.assertEqual(failed, [])

    def test_labels_are_not_trivially_separable(self):
        df = generate(GeneratorConfig(n_samples=5_000))
        self.assertTrue(0.08 < df[TARGET].mean() < 0.25)
        # Aynı metrik aralığında hem riskli hem risksiz tedarikçi bulunmalı.
        mid = df[(df["averageDeliveryDelayDays"].between(1, 2))]
        self.assertTrue(0.1 < mid[TARGET].mean() < 0.9)


if __name__ == "__main__":
    unittest.main()
