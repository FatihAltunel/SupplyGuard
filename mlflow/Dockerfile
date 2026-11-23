FROM python:3.10-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1
RUN pip install --disable-pip-version-check mlflow==3.18.0
EXPOSE 5000
CMD ["mlflow", "server", "--host", "0.0.0.0", "--port", "5000", \
     "--allowed-hosts", "mlflow:5000,supplyguard-mlflow:5000,localhost:*,127.0.0.1:*", \
     "--backend-store-uri", "sqlite:////mlruns/mlflow.db", \
     "--serve-artifacts", "--artifacts-destination", "/mlruns/artifacts"]
