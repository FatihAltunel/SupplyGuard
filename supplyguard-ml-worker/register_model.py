"""Register the trained JIT model with the local MLflow server."""

from pathlib import Path

import joblib
import mlflow
import mlflow.lightgbm


TRACKING_URI = "http://localhost:5000"
EXPERIMENT_NAME = "SupplyGuard_JIT_Analysis"
MODEL_NAME = "SupplyGuard_JIT_Model"
MODEL_PATH = Path(__file__).resolve().parent / "lgbm_jit_v1.0.joblib"


def main() -> None:
    if not MODEL_PATH.is_file():
        raise FileNotFoundError(f"Model file not found: {MODEL_PATH}")

    mlflow.set_tracking_uri(TRACKING_URI)
    mlflow.set_registry_uri(TRACKING_URI)
    mlflow.set_experiment(EXPERIMENT_NAME)

    model = joblib.load(MODEL_PATH)

    with mlflow.start_run(
        run_name="register_lgbm_jit_v1.0",
        tags={
            "Model_Type": "LightGBM",
            "Industry": "Automotive JIT",
            "Environment": "Production",
        },
    ) as run:
        model_info = mlflow.lightgbm.log_model(
            lgb_model=model,
            name="model",
            registered_model_name=MODEL_NAME,
        )

        print(f"Run ID: {run.info.run_id}")
        print(f"Registered model: {MODEL_NAME}")
        print(f"Model version: {model_info.registered_model_version}")
        print(f"Model URI: {model_info.model_uri}")


if __name__ == "__main__":
    main()
