from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import PurePosixPath


def _get_int(name: str, default: int) -> int:
    value = os.getenv(name)
    return int(value) if value else default


@dataclass(frozen=True)
class RK3588Environment:
    host: str = os.getenv("AURORA_RK3588_HOST", "10.127.135.143")
    user: str = os.getenv("AURORA_RK3588_USER", "linaro")
    service_port: int = _get_int("AURORA_RK3588_PORT", 8080)
    model_name: str = os.getenv("AURORA_RK3588_MODEL", "llama3.2:3b")
    rkllama_root: PurePosixPath = PurePosixPath(
        os.getenv("AURORA_RK3588_RKLLAMA_ROOT", "/home/linaro/rkllama-src")
    )
    model_file: PurePosixPath = PurePosixPath(
        os.getenv(
            "AURORA_RK3588_MODEL_FILE",
            "/home/linaro/Llama-3.2-3B-Instruct_w8a8_g128_rk3588.rkllm",
        )
    )
    models_dir: PurePosixPath = PurePosixPath(
        os.getenv("AURORA_RK3588_MODELS_DIR", "/home/linaro/rkllama-src/models")
    )
    dashboard_path: str = os.getenv("AURORA_RK3588_DASHBOARD_PATH", "/dashboard")
    status_api_path: str = os.getenv(
        "AURORA_RK3588_STATUS_API_PATH", "/api/dashboard/status"
    )
    generate_api_path: str = os.getenv(
        "AURORA_RK3588_GENERATE_API_PATH", "/api/generate"
    )
    ssh_password_env_var: str = "AURORA_RK3588_PASSWORD"

    @property
    def ssh_target(self) -> str:
        return f"{self.user}@{self.host}"

    @property
    def base_url(self) -> str:
        return f"http://{self.host}:{self.service_port}"

    @property
    def dashboard_url(self) -> str:
        return f"{self.base_url}{self.dashboard_path}"

    @property
    def status_api_url(self) -> str:
        return f"{self.base_url}{self.status_api_path}"

    @property
    def generate_url(self) -> str:
        return f"{self.base_url}{self.generate_api_path}"


RK3588_ENV = RK3588Environment()
