"""@challenger sürümünü @champion ile aynı referans veride kıyaslar; kapılar geçerse terfi ettirir.

Kullanım (depo kökünden):
    python supplyguard-ml-worker/training/promote.py --status          # hangi alias hangi sürümde
    python supplyguard-ml-worker/training/promote.py --bootstrap 1     # ilk kurulum: v1'i champion yap
    python supplyguard-ml-worker/training/promote.py --dry-run         # kıyasla, alias'a dokunma
    python supplyguard-ml-worker/training/promote.py                   # kıyasla, geçerse terfi et
    python supplyguard-ml-worker/training/promote.py --rollback        # champion -> previous_champion

Worker modeli yalnızca başlangıçta yükler. Alias değişince:
    docker compose restart ml-worker

Neden alias? Worker 'latest' yüklerse, kim yeni bir sürüm kaydederse (deney bile olsa)
bir sonraki restart'ta o sürüm canlıya çıkar. '@champion' ise yalnızca bu betiğin bilinçli
kararıyla hareket eder; geri alma da tek komuttur.
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

import mlflow
import mlflow.artifacts
import mlflow.pyfunc
import pandas as pd
from mlflow import MlflowClient
from mlflow.exceptions import MlflowException

sys.path.insert(0, str(Path(__file__).resolve().parent))
from data_generator import FEATURE_NAMES, TARGET  # noqa: E402
from model_checks import Gates, compare_to_champion, run_checks  # noqa: E402

MODEL_NAME = "SupplyGuard_JIT_Model"
CHAMPION, CHALLENGER, PREVIOUS = "champion", "challenger", "previous_champion"
PROMOTION_EXPERIMENT = "SupplyGuard_JIT_Promotions"


def alias_version(client: MlflowClient, alias: str) -> str | None:
    try:
        return client.get_model_version_by_alias(MODEL_NAME, alias).version
    except MlflowException:
        return None


def load_classifier(version: str):
    # Worker ile birebir aynı yükleme yolu: pyfunc -> get_raw_model(). Burada yüklenemeyen
    # sürüm worker'da da yüklenemez; bunu terfiden ÖNCE yakalamak istiyoruz.
    return mlflow.pyfunc.load_model(f"models:/{MODEL_NAME}/{version}").get_raw_model()


def reference_data(client: MlflowClient, version: str) -> tuple[pd.DataFrame, pd.Series]:
    run_id = client.get_model_version(MODEL_NAME, version).run_id
    path = mlflow.artifacts.download_artifacts(run_id=run_id, artifact_path="reference/holdout.csv")
    frame = pd.read_csv(path)
    return frame[list(FEATURE_NAMES)], frame[TARGET].to_numpy()


def print_status(client: MlflowClient) -> None:
    for alias in (CHAMPION, CHALLENGER, PREVIOUS):
        print(f"@{alias:<18} -> v{alias_version(client, alias) or '-'}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tracking-uri", default=os.getenv("MLFLOW_TRACKING_URI", "http://localhost:5000"))
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--status", action="store_true")
    group.add_argument("--bootstrap", metavar="VERSION", help="Kapı çalıştırmadan bu sürümü champion yap")
    group.add_argument("--rollback", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    mlflow.set_tracking_uri(args.tracking_uri)
    mlflow.set_registry_uri(args.tracking_uri)
    client = MlflowClient()

    if args.status:
        print_status(client)
        return 0

    if args.bootstrap:
        load_classifier(args.bootstrap)  # en azından yüklenebildiğini doğrula
        client.set_registered_model_alias(MODEL_NAME, CHAMPION, args.bootstrap)
        print(f"v{args.bootstrap} -> @{CHAMPION} (bootstrap, kapı çalıştırılmadı)")
        return 0

    if args.rollback:
        previous = alias_version(client, PREVIOUS)
        if previous is None:
            print("@previous_champion yok; geri alınacak sürüm bulunamadı.")
            return 1
        current = alias_version(client, CHAMPION)
        client.set_registered_model_alias(MODEL_NAME, CHAMPION, previous)
        if current:
            client.set_registered_model_alias(MODEL_NAME, PREVIOUS, current)
        print(f"Geri alındı: @{CHAMPION} v{current} -> v{previous}. Şimdi: docker compose restart ml-worker")
        return 0

    challenger_v = alias_version(client, CHALLENGER)
    if challenger_v is None:
        print("@challenger yok. Önce train.py çalıştırın.")
        return 1
    champion_v = alias_version(client, CHAMPION)
    if champion_v == challenger_v:
        print(f"v{challenger_v} zaten champion.")
        return 0

    gates = Gates()
    # Referans veri challenger'ın run'ından gelir; iki model AYNI veride ölçülür.
    x_ref, y_ref = reference_data(client, challenger_v)
    challenger_report = run_checks(load_classifier(challenger_v), x_ref, y_ref, gates)
    failures = list(challenger_report.failures)
    champion_report = None
    if champion_v is not None:
        champion_report = run_checks(load_classifier(champion_v), x_ref, y_ref, gates)
        failures += compare_to_champion(challenger_report, champion_report, gates)

    keys = ["auc", "pr_auc", "brier", "ece", "mean_prediction", "extreme_share", "golden_pass_rate"]
    print(f"\n{'metrik':<18}{'champion v' + str(champion_v or '-'):>16}{'challenger v' + challenger_v:>18}")
    for key in keys:
        old = f"{champion_report.metrics[key]:.4f}" if champion_report else "-"
        print(f"{key:<18}{old:>16}{challenger_report.metrics[key]:>18.4f}")
    print("\nChallenger golden case'leri:")
    for g in challenger_report.golden:
        print(f"  {'OK ' if g['passed'] else 'HATA'} {g['name']:<34} %{g['risk'] * 100:5.1f}")

    decision = "approved" if not failures else "rejected"
    mlflow.set_experiment(PROMOTION_EXPERIMENT)
    with mlflow.start_run(run_name=f"promote v{challenger_v} over v{champion_v or '-'}"):
        mlflow.set_tags({"challenger_version": challenger_v, "champion_version": champion_v or "none",
                         "decision": decision, "dry_run": str(args.dry_run).lower()})
        mlflow.log_metrics({f"challenger_{k}": v for k, v in challenger_report.metrics.items()})
        if champion_report:
            mlflow.log_metrics({f"champion_{k}": v for k, v in champion_report.metrics.items()})
        mlflow.log_dict({"failures": failures, "golden": challenger_report.golden}, "decision.json")

    if failures:
        print("\nTERFİ REDDEDİLDİ:")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    if args.dry_run:
        print(f"\n--dry-run: v{challenger_v} terfi edilebilir, alias değiştirilmedi.")
        return 0

    if champion_v is not None:
        client.set_registered_model_alias(MODEL_NAME, PREVIOUS, champion_v)
    client.set_registered_model_alias(MODEL_NAME, CHAMPION, challenger_v)
    client.delete_registered_model_alias(MODEL_NAME, CHALLENGER)
    client.set_model_version_tag(MODEL_NAME, challenger_v, "validation_status", "approved")
    print(f"\nv{challenger_v} -> @{CHAMPION} (önceki v{champion_v or '-'} -> @{PREVIOUS})")
    print("Şimdi: docker compose restart ml-worker")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
