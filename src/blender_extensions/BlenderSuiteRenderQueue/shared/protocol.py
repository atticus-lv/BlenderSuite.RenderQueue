from __future__ import annotations

import json


BSRQ_WORKER_MODE = "BSRQ_WORKER_MODE"
BSRQ_WORKER_ENDPOINT = "BSRQ_WORKER_ENDPOINT"
BSRQ_WORKER_TOKEN = "BSRQ_WORKER_TOKEN"
BSRQ_APP_INSTANCE_ID = "BSRQ_APP_INSTANCE_ID"
BSRQ_LOG_PATH = "BSRQ_LOG_PATH"


def parse_endpoint(endpoint: str) -> tuple[str, int]:
    host, _, port = endpoint.rpartition(":")
    if not host or not port:
        raise ValueError(f"Invalid worker endpoint: {endpoint}")
    return host, int(port)


def make_response(request_id, ok: bool, worker_state: str, payload=None, error: str | None = None) -> dict:
    response = {
        "request_id": request_id,
        "ok": ok,
        "worker_state": worker_state,
        "payload": payload or {},
    }
    if error:
        response["error"] = error
    return response


def encode_message(message: dict) -> bytes:
    return (json.dumps(message, ensure_ascii=False) + "\n").encode("utf-8")
