from __future__ import annotations

import bpy

from ..shared.protocol import make_response
from . import session


def handle_request(runtime, request: dict) -> dict:
    request_id = request.get("request_id")
    command = request.get("command")
    payload = request.get("payload") or {}

    try:
        runtime.state.request_count += 1

        if command == "ping":
            runtime.state.refresh_from_context()
            payload_out = runtime.state.snapshot_payload()
        elif command == "load_file":
            payload_out = session.load_file(runtime.state, payload.get("filepath", ""))
        elif command == "query_file_info":
            payload_out = session.query_file_info(runtime.state)
        elif command == "render_task":
            payload_out = session.render_task(runtime.state, payload)
        elif command == "cancel_current":
            payload_out = session.cancel_current(runtime.state)
        elif command == "shutdown":
            runtime.request_shutdown()
            payload_out = {"scheduled": True}
        else:
            raise ValueError(f"Unknown worker command: {command}")

        return make_response(request_id, True, runtime.state.status, payload_out)
    except Exception as exc:
        runtime.state.set_status("error", str(exc))
        return make_response(request_id, False, runtime.state.status, error=str(exc))
