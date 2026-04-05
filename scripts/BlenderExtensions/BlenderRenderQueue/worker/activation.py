from __future__ import annotations

import os
from dataclasses import dataclass

from ..shared.protocol import (
    BRQ_APP_INSTANCE_ID,
    BRQ_LOG_PATH,
    BRQ_WORKER_ENDPOINT,
    BRQ_WORKER_MODE,
    BRQ_WORKER_TOKEN,
    parse_endpoint,
)


@dataclass
class ActivationContext:
    host: str
    port: int
    token: str
    app_instance_id: str
    log_path: str | None


def get_activation_context() -> ActivationContext | None:
    if os.environ.get(BRQ_WORKER_MODE) != "1":
        return None

    endpoint = os.environ.get(BRQ_WORKER_ENDPOINT, "").strip()
    token = os.environ.get(BRQ_WORKER_TOKEN, "").strip()
    app_instance_id = os.environ.get(BRQ_APP_INSTANCE_ID, "").strip()
    log_path = os.environ.get(BRQ_LOG_PATH, "").strip() or None

    if not endpoint or not token:
        return None

    host, port = parse_endpoint(endpoint)
    return ActivationContext(
        host=host,
        port=port,
        token=token,
        app_instance_id=app_instance_id,
        log_path=log_path,
    )
