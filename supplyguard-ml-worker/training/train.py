"""SupplyGuard JIT modelini eğitir, MLflow'a loglar ve 'challenger' olarak kaydeder.

Kullanım (depo kökünden, ml-worker imajının İÇİNDE; böylece sklearn/lightgbm/mlflow
sürümleri modeli yükleyecek worker ile birebir aynı olur):

    docker compose run --rm --no-deps -v "${PWD}/supplyguard-ml-worker:/work:ro" -w /work \\
        ml-worker python training/train.py --tracking-uri http://mlflow:5000
    # ... training/train.py --tracking-uri http://mlflow:5000 --no-register   # sadece deney

Ayrıntı ve PowerShell kısayolu (Invoke-Training): training/README.md

Akış:
    veri üret -> train/valid/holdout böl -> LightGBM (monoton + early stopping)
    -> kalite kapıları -> MLflow run (params, metrics, artifacts, dataset, signature)
    -> Registry'de yeni sürüm -> kapılar geçtiyse '@challenger' alias'ı

Champion'a terfi bu betiğin işi DEĞİLDİR; bunu promote.py yapar. Eğitim ile canlıya
alma kararını ayırmak, yanlışlıkla eğitilen bir modelin üretime sızmasını engeller.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

import lightgbm as lgb
import mlflow
import mlflow.data
import mlflow.lightgbm
import numpy as np
import pandas as pd
from mlflow import MlflowClient
from mlflow.models import infer_signature
from sklearn.model_selection import train_test_split

sys.path.insert(0, str(Path(__file__).resolve().parent))
from data_generator import FEATURE_NAMES, MONOTONE_DIRECTIONS, TARGET, GeneratorConfig, generate  # noqa: E402
from model_checks import Gates, run_checks  # noqa: E402

MODEL_NAME = "SupplyGuard_JIT_Model"
EXPERIMENT_NAME = "SupplyGuard_JIT_Analysis"
CHALLENGER_ALIAS = "challenger"

MODEL_PARAMS = {
    "objective": "binary",
    "n_estimators": 3000,          # üst sınır; gerçek ağaç sayısını early stopping belirler
    "learning_rate": 0.03,
    "num_leaves": 15,              # küçük ağaçlar: 6 özellik için yeterli, ezberi azaltır
    "min_child_samples": 50,       # yaprak başına en az 50 tedarikçi: tekil uç noktaları ezberlemez
    "subsample": 0.8,
    "subsample_freq": 1,
    "colsample_bytree": 0.9,
    "reg_lambda": 1.0,
    "monotone_constraints": [MONOTONE_DIRECTIONS[f] for f in FEATURE_NAMES],
    "monotone_constraints_method": "advanced",
    # class_weight BİLEREK yok: 'balanced' olasılıkları gerçek orandan yukarı kaydırır
    # (v1'de ortalama tahmin gerçek oranın ~2.5 katıydı). Dengesizlik eşik seçimiyle çözülür.
    "random_state": 42,
    "verbose": -1,
}
EARLY_STOPPING_ROUNDS = 100


def git_metadata() -> dict[str, str]:
    root = Path(__file__).resolve().parents[2]
    try:
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=root, text=True).strip())
        return {"git_commit": commit, "git_dirty": str(dirty).lower()}
    except (OSError, subprocess.CalledProcessError):
        return {"git_commit": "unknown", "git_dirty": "unknown"}


def shap_additivity_error(model: lgb.LGBMClassifier, x: pd.DataFrame) -> float:
    """Worker'daki SHAP yolu bu modelde çalışıyor mu? BaseValue + ΣSHAP = ham log-odds olmalı."""
    import shap

    explainer = shap.TreeExplainer(model, model_output="raw", feature_perturbation="tree_path_dependent")
    values = np.asarray(explainer.shap_values(x), dtype=float)
    if values.ndim == 3:  # bazı shap sürümleri (n, f, 2) döndürür
        values = values[:, :, 1]
    base = float(np.asarray(explainer.expected_value, dtype=float).reshape(-1)[-1])
    raw = model.predict(x, raw_score=True)
    return float(np.max(np.abs(base + values.sum(axis=1) - raw)))


def log_calibration_plot(y: np.ndarray, p: np.ndarray) -> None:
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print("matplotlib yok; kalibrasyon grafiği atlandı (metrikler yine loglandı).")
        return
    bins = np.linspace(0, 1, 11)
    idx = np.clip(np.digitize(p, bins) - 1, 0, 9)
    centers = [p[idx == b].mean() for b in range(10) if (idx == b).any()]
    actual = [y[idx == b].mean() for b in range(10) if (idx == b).any()]
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(10, 4))
    ax1.plot([0, 1], [0, 1], "--", color="grey", label="Mükemmel kalibrasyon")
    ax1.plot(centers, actual, "o-", label="Model")
    ax1.set_xlabel("Tahmin edilen risk"), ax1.set_ylabel("Gerçekleşen oran"), ax1.legend()
    ax1.set_title("Kalibrasyon")
    ax2.hist(p, bins=40)
    ax2.set_xlabel("Tahmin edilen risk"), ax2.set_title("Tahmin dağılımı")
    fig.tight_layout()
    mlflow.log_figure(fig, "plots/calibration.png")
    plt.close(fig)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tracking-uri", default=os.getenv("MLFLOW_TRACKING_URI", "http://localhost:5000"))
    parser.add_argument("--no-register", action="store_true", help="Registry'ye kaydetmeden sadece deney logla")
    parser.add_argument("--run-name", default=None)
    args = parser.parse_args()

    mlflow.set_tracking_uri(args.tracking_uri)
    mlflow.set_registry_uri(args.tracking_uri)
    mlflow.set_experiment(EXPERIMENT_NAME)

    cfg = GeneratorConfig()
    gates = Gates()
    df = generate(cfg)
    x, y = df[list(FEATURE_NAMES)], df[TARGET].to_numpy()
    # %60 eğitim / %20 early-stopping doğrulama / %20 referans (kalite kapıları + champion kıyası).
    x_rest, x_hold, y_rest, y_hold = train_test_split(x, y, test_size=0.2, stratify=y, random_state=cfg.seed)
    x_train, x_valid, y_train, y_valid = train_test_split(
        x_rest, y_rest, test_size=0.25, stratify=y_rest, random_state=cfg.seed)

    with mlflow.start_run(run_name=args.run_name or "train_lgbm_jit_v2") as run:
        mlflow.set_tags({"model_type": "LightGBM", "data": "synthetic-v2", "stage": "candidate",
                         **git_metadata()})
        mlflow.log_params({f"lgbm_{k}": v for k, v in MODEL_PARAMS.items()})
        mlflow.log_params(cfg.as_params())
        mlflow.log_params({f"gate_{k}": v for k, v in gates.__dict__.items()})
        mlflow.log_input(mlflow.data.from_pandas(df, name="supplyguard-synthetic-v2", targets=TARGET),
                         context="training")

        model = lgb.LGBMClassifier(**MODEL_PARAMS)
        model.fit(x_train, y_train, eval_set=[(x_valid, y_valid)], eval_metric="binary_logloss",
                  callbacks=[lgb.early_stopping(EARLY_STOPPING_ROUNDS, verbose=False)])
        mlflow.log_metric("best_iteration", model.best_iteration_)

        report = run_checks(model, x_hold, y_hold, gates)
        additivity = shap_additivity_error(model, x_hold.head(200))
        report.metrics["shap_additivity_max_error"] = additivity
        if additivity > 1e-4:
            report.failures.append(f"SHAP toplamı ham skorla uyuşmuyor (max hata {additivity:.2e})")

        mlflow.log_metrics(report.metrics)
        mlflow.log_dict({"golden_cases": report.golden, "failures": report.failures}, "checks/report.json")
        log_calibration_plot(y_hold, model.predict_proba(x_hold)[:, 1])
        importance = dict(zip(FEATURE_NAMES, map(int, model.booster_.feature_importance("gain"))))
        mlflow.log_dict(importance, "checks/feature_importance_gain.json")

        with tempfile.TemporaryDirectory() as tmp:
            holdout_path = Path(tmp) / "holdout.csv"
            x_hold.assign(**{TARGET: y_hold}).to_csv(holdout_path, index=False)
            mlflow.log_artifact(str(holdout_path), artifact_path="reference")

        signature = infer_signature(x_hold, model.predict(x_hold))
        model_info = mlflow.lightgbm.log_model(
            lgb_model=model, name="model", signature=signature, input_example=x_hold.head(3),
            registered_model_name=None if args.no_register else MODEL_NAME,
        )

        print(f"\nRun ID : {run.info.run_id}")
        print(f"Ağaç   : {model.best_iteration_}")
        print(json.dumps({k: round(v, 4) for k, v in report.metrics.items()
                          if not k.startswith("monotonic")}, indent=2, ensure_ascii=False))
        for g in report.golden:
            print(f"  {'OK ' if g['passed'] else 'HATA'} {g['name']:<34} risk=%{g['risk'] * 100:5.1f}"
                  f"  beklenen [%{g['min'] * 100:.0f}, %{g['max'] * 100:.0f}]")

        if args.no_register:
            print("\n--no-register: Registry'ye kaydedilmedi.")
            return 0 if report.passed else 1

        version = model_info.registered_model_version
        client = MlflowClient()
        status = "passed" if report.passed else "failed"
        client.set_model_version_tag(MODEL_NAME, version, "validation_status", status)
        client.set_model_version_tag(MODEL_NAME, version, "data", "synthetic-v2")
        if report.passed:
            client.set_registered_model_alias(MODEL_NAME, CHALLENGER_ALIAS, version)
            print(f"\n{MODEL_NAME} v{version} kapıları GEÇTİ -> @{CHALLENGER_ALIAS}")
            print("Sıradaki adım: training/promote.py (ml-worker imajında; bkz. training/README.md)")
            return 0
        print(f"\n{MODEL_NAME} v{version} kapılardan KALDI; alias verilmedi:")
        for failure in report.failures:
            print(f"  - {failure}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
