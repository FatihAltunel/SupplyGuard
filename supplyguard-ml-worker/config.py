"""Environment settings; credentials are masked in repr."""
from aio_pika.connection import make_url
from pydantic import Field, SecretStr, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    mlflow_tracking_uri: str = "http://localhost:5000"
    # Geçici test için ör. models:/SupplyGuard_JIT_Model@challenger veya models:/SupplyGuard_JIT_Model/3
    mlflow_model_uri: str = Field(default="models:/SupplyGuard_JIT_Model@champion", min_length=1)
    rabbitmq_url: SecretStr = SecretStr("amqp://guest:guest@localhost/")
    # Preserve the existing Docker Compose environment. An explicit URL wins.
    rabbitmq_host: str = "localhost"
    rabbitmq_port: int = Field(default=5672, ge=1, le=65535)
    rabbitmq_user: str = "guest"
    rabbitmq_password: SecretStr = SecretStr("guest")
    rabbitmq_vhost: str = "/"
    rabbitmq_use_tls: bool = False
    rabbitmq_request_queue: str = Field(default="supplyguard.risk.requests", min_length=1)
    rabbitmq_response_queue: str = Field(default="supplyguard.risk.responses", min_length=1)
    rabbitmq_publish_timeout: float = Field(default=10, gt=0, allow_inf_nan=False)
    rabbitmq_retry_seconds: float = Field(default=5, gt=0, allow_inf_nan=False)

    @model_validator(mode="after")
    def distinct_queues(self) -> "Settings":
        components = {
            "rabbitmq_host", "rabbitmq_port", "rabbitmq_user", "rabbitmq_password",
            "rabbitmq_vhost", "rabbitmq_use_tls",
        }
        if "rabbitmq_url" not in self.model_fields_set and components & self.model_fields_set:
            self.rabbitmq_url = SecretStr(str(make_url(
                host=self.rabbitmq_host, port=self.rabbitmq_port, login=self.rabbitmq_user,
                password=self.rabbitmq_password.get_secret_value(),
                virtualhost=self.rabbitmq_vhost, ssl=self.rabbitmq_use_tls,
            )))
        if self.rabbitmq_request_queue == self.rabbitmq_response_queue:
            raise ValueError("Request and response queues must be different.")
        return self
