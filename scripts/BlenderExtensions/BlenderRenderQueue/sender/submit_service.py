from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import uuid
from pathlib import Path
from shutil import which

import bpy

from ..shared.paths import get_submission_endpoint_path


CONNECT_TIMEOUT_SECONDS = 2.0
STARTUP_TIMEOUT_SECONDS = 30.0
STARTUP_POLL_INTERVAL_SECONDS = 0.4


def _get_preferences():
    package_name = __package__ or ""
    package_parts = package_name.split(".")

    candidates = []
    for length in range(len(package_parts), 0, -1):
        candidate = ".".join(package_parts[:length])
        if candidate:
            candidates.append(candidate)

    if package_parts and package_parts[0] == "bl_ext" and len(package_parts) >= 3:
        candidates.append(".".join(package_parts[:3]))

    if package_parts:
        candidates.append(package_parts[-1])

    seen = set()
    for candidate in candidates:
        if candidate in seen:
            continue
        seen.add(candidate)

        addon = bpy.context.preferences.addons.get(candidate)
        if addon:
            return addon.preferences

    return None


def _find_app_launch_target() -> str | None:
    prefs = _get_preferences()
    app_launch_path = prefs.app_launch_path if prefs else ""
    return find_app_launch_target(app_launch_path)


def find_app_launch_target(app_launch_path: str = "") -> str | None:
    if app_launch_path and os.path.exists(app_launch_path):
        return app_launch_path

    explicit_path = os.environ.get("BRQ_APP_PATH")
    if explicit_path and os.path.exists(explicit_path):
        return explicit_path

    executable_names = ["BlenderRenderQueue.exe", "BlenderRenderQueue"]
    for executable_name in executable_names:
        found = which(executable_name)
        if found and os.path.isfile(found):
            return found

    if sys.platform == "win32":
        for env_var in ("ProgramFiles", "ProgramFiles(x86)"):
            base = os.environ.get(env_var)
            if base:
                candidate = Path(base) / "BlenderRenderQueue" / "BlenderRenderQueue.exe"
                if candidate.is_file():
                    return str(candidate)
    elif sys.platform == "darwin":
        for candidate in (
            Path("/Applications/BlenderRenderQueue.app"),
            Path.home() / "Applications" / "BlenderRenderQueue.app",
        ):
            if candidate.exists():
                return str(candidate)

    return None


def _launch_app(launch_target: str):
    popen_kwargs = {
        "stdin": subprocess.DEVNULL,
        "stdout": subprocess.DEVNULL,
        "stderr": subprocess.DEVNULL,
    }

    if sys.platform == "win32":
        create_no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        subprocess.Popen(
            f'cmd /c start "" "{launch_target}"',
            shell=True,
            creationflags=create_no_window,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return

    if launch_target.endswith(".app"):
        subprocess.Popen(["open", "-a", launch_target], start_new_session=True, **popen_kwargs)
        return

    if launch_target.endswith(".dll"):
        subprocess.Popen(["dotnet", launch_target], start_new_session=True, **popen_kwargs)
        return

    subprocess.Popen([launch_target], start_new_session=True, **popen_kwargs)


def _read_endpoint_info() -> dict | None:
    endpoint_path = get_submission_endpoint_path()
    if not endpoint_path.exists():
        return None

    try:
        return json.loads(endpoint_path.read_text(encoding="utf-8"))
    except Exception:
        return None


def _should_auto_start_queue() -> bool:
    env_value = os.environ.get("BRQ_AUTO_START_QUEUE", "").strip().lower()
    if env_value in {"1", "true", "yes", "on"}:
        return True

    prefs = _get_preferences()
    return bool(prefs and prefs.auto_start_queue_after_submit)


def _send_request(endpoint: dict, command: str, payload: dict | None = None) -> dict:
    request = {
        "request_id": uuid.uuid4().hex,
        "command": command,
        "token": endpoint.get("token", ""),
        "payload": payload,
    }

    with socket.create_connection(
        (endpoint.get("host", "127.0.0.1"), int(endpoint.get("port", 0))),
        timeout=CONNECT_TIMEOUT_SECONDS,
    ) as conn:
        conn.settimeout(CONNECT_TIMEOUT_SECONDS)
        request_data = (json.dumps(request, ensure_ascii=False) + "\n").encode("utf-8")
        conn.sendall(request_data)

        response_chunks: list[bytes] = []
        while True:
            chunk = conn.recv(4096)
            if not chunk:
                break
            response_chunks.append(chunk)
            if b"\n" in chunk:
                break

    response_text = b"".join(response_chunks).decode("utf-8").strip()
    if not response_text:
        raise RuntimeError("Desktop app returned an empty submission response.")

    return json.loads(response_text)


def _wait_for_endpoint(report_callback) -> dict:
    return wait_for_endpoint(report_callback)


def wait_for_endpoint(report_callback=None) -> dict:
    deadline = time.time() + STARTUP_TIMEOUT_SECONDS
    last_error = "Desktop app did not publish a submission endpoint."

    while time.time() < deadline:
        endpoint = _read_endpoint_info()
        if endpoint:
            try:
                response = _send_request(endpoint, "ping")
                if response.get("ok"):
                    return endpoint
                last_error = response.get("message") or last_error
            except Exception as exc:
                last_error = str(exc)

        time.sleep(STARTUP_POLL_INTERVAL_SECONDS)

    if report_callback is not None:
        report_callback({"WARNING"}, f"Failed to connect to BlenderRenderQueue after startup: {last_error}")
    raise RuntimeError(last_error)


def _ensure_endpoint(report_callback) -> dict:
    prefs = _get_preferences()
    auto_start_app = bool(prefs and prefs.auto_start_app)
    app_launch_path = prefs.app_launch_path if prefs else ""
    return ensure_endpoint(auto_start_app, app_launch_path, report_callback)


def ensure_endpoint(auto_start_app: bool, app_launch_path: str = "", report_callback=None) -> dict:
    endpoint = _read_endpoint_info()
    if endpoint:
        try:
            response = _send_request(endpoint, "ping")
            if response.get("ok"):
                return endpoint
        except Exception:
            pass

    if not auto_start_app:
        raise RuntimeError("BlenderRenderQueue is not running and auto-start is disabled.")

    launch_target = find_app_launch_target(app_launch_path)
    if not launch_target:
        raise RuntimeError("BlenderRenderQueue is not running and no launch target was found.")

    _launch_app(launch_target)
    return wait_for_endpoint(report_callback)


def submit_task_payload(
    blend_file_path: str,
    scene_name: str,
    override_frame_range: bool,
    frame_start: int,
    frame_end: int,
    *,
    auto_start_app: bool,
    app_launch_path: str = "",
    auto_start_queue: bool = False,
    report_callback=None,
) -> dict:
    if not blend_file_path:
        raise RuntimeError("Save the .blend file before submitting it to BlenderRenderQueue.")

    endpoint = ensure_endpoint(auto_start_app, app_launch_path, report_callback)

    payload = {
        "filepath": blend_file_path,
        "filename": os.path.basename(blend_file_path),
        "scene_name": scene_name,
        "override_frame_range": override_frame_range,
        "frame_start": frame_start,
        "frame_end": frame_end,
        "submitted_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }

    response = _send_request(endpoint, "submit_task", payload)
    if not response.get("ok"):
        raise RuntimeError(response.get("message") or "Desktop app rejected the submission.")

    if auto_start_queue:
        start_response = _send_request(endpoint, "start_queue")
        if not start_response.get("ok"):
            raise RuntimeError(start_response.get("message") or "Desktop app rejected the queue start request.")

    return response


def submit_task(
    scene_name: str,
    override_frame_range: bool,
    frame_start: int,
    frame_end: int,
    report_callback,
) -> dict:
    blend_file_path = bpy.data.filepath
    if not blend_file_path:
        raise RuntimeError("Save the .blend file before submitting it to BlenderRenderQueue.")

    prefs = _get_preferences()
    auto_start_app = bool(prefs and prefs.auto_start_app)
    app_launch_path = prefs.app_launch_path if prefs else ""
    auto_start_queue = _should_auto_start_queue()

    return submit_task_payload(
        blend_file_path,
        scene_name,
        override_frame_range,
        frame_start,
        frame_end,
        auto_start_app=auto_start_app,
        app_launch_path=app_launch_path,
        auto_start_queue=auto_start_queue,
        report_callback=report_callback,
    )
