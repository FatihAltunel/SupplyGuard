import mlflow
import mlflow.lightgbm
mlflow.set_tracking_uri("http://localhost:5000")
mlflow.set_registry_uri("http://localhost:5000")
model = mlflow.lightgbm.load_model("models:/SupplyGuard_JIT_Model/latest")
print("Model MLflow'dan başarıyla yüklendi!")
