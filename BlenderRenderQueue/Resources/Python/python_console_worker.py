import json
import os
import queue
import socket
import threading
import time
import traceback
from datetime import datetime, timezone

import bpy
from bpy.app.handlers import persistent


BRQ_RUNTIME = None


def _utc_now():
    return datetime.now(timezone.utc).isoformat()


class Logger:
    def __init__(self, log_path):
        self._log_path = log_path or ""
        self._lock = threading.RLock()

    def write(self, message):
        if not self._log_path:
            return
        line = f"[BRQ][{_utc_now()}] {message}\n"
        with self._lock:
            os.makedirs(os.path.dirname(self._log_path), exist_ok=True)
            with open(self._log_path, "a", encoding="utf-8") as handle:
                handle.write(line)


class WorkerState:
    def __init__(self, endpoint, app_instance_id):
        self.endpoint = endpoint
        self.app_instance_id = app_instance_id
        self.status = "starting"
        self.last_error = ""
        self.request_count = 0
        self.shutdown_requested = False
        self.current_file = ""
        self.active_scene = ""
        self.scenes = []
        self.camera = ""
        self.frame_start = 0
        self.frame_end = 0
        self.output_path = ""
        self.is_saved = False
        self.render_started_at = None
        self.last_heartbeat_at = None
        self.output_verified = False
        self._lock = threading.RLock()

    def set_status(self, status, error=""):
        with self._lock:
            self.status = status
            self.last_error = error
            if status != "rendering":
                self.render_started_at = None

    def begin_render(self):
        with self._lock:
            self.status = "rendering"
            self.last_error = ""
            self.render_started_at = _utc_now()
            self.output_verified = False

    def set_output_verified(self, verified):
        with self._lock:
            self.output_verified = bool(verified)

    def touch_heartbeat(self):
        with self._lock:
            self.last_heartbeat_at = _utc_now()

    def refresh_from_context(self):
        with self._lock:
            scene = self._resolve_scene()
            self.current_file = self._safe_get_filepath()
            self.scenes = self._safe_get_scene_names()
            self.active_scene = scene.name if scene else ""
            self.camera = scene.camera.name if scene and getattr(scene, "camera", None) else ""
            self.frame_start = scene.frame_start if scene else 0
            self.frame_end = scene.frame_end if scene else 0
            self.output_path = scene.render.filepath if scene else ""
            self.is_saved = bool(self.current_file)

    def snapshot_payload(self):
        with self._lock:
            return {
                "current_file": self.current_file,
                "active_scene": self.active_scene,
                "scenes": list(self.scenes),
                "camera": self.camera,
                "frame_start": self.frame_start,
                "frame_end": self.frame_end,
                "output_path": self.output_path,
                "is_saved": self.is_saved,
                "render_started_at": self.render_started_at,
                "last_heartbeat_at": self.last_heartbeat_at,
                "last_error": self.last_error,
                "output_verified": self.output_verified,
                "request_count": self.request_count,
                "app_instance_id": self.app_instance_id,
                "endpoint": self.endpoint,
            }

    @staticmethod
    def _resolve_scene():
        try:
            scene = getattr(bpy.context, "scene", None)
            if scene is not None:
                return scene
        except Exception:
            pass

        try:
            scenes = list(getattr(bpy.data, "scenes", []))
            return scenes[0] if scenes else None
        except Exception:
            return None

    @staticmethod
    def _safe_get_filepath():
        try:
            return getattr(bpy.data, "filepath", "") or ""
        except Exception:
            return ""

    @staticmethod
    def _safe_get_scene_names():
        try:
            return [scene.name for scene in getattr(bpy.data, "scenes", [])]
        except Exception:
            return []


class PendingRequest:
    def __init__(self, request):
        self.request = request
        self.event = threading.Event()
        self.response = None


def make_response(request_id, ok, worker_state, payload=None, error=None):
    response = {
        "request_id": request_id,
        "ok": ok,
        "worker_state": worker_state,
        "payload": payload or {},
    }
    if error:
        response["error"] = error
    return response


def encode_message(message):
    return (json.dumps(message, ensure_ascii=False) + "\n").encode("utf-8")


class WorkerServer:
    def __init__(self, host, port, token, runtime):
        self._host = host
        self._port = port
        self._token = token
        self._runtime = runtime
        self._pending_requests = queue.Queue()
        self._stop_event = threading.Event()
        self._server_socket = None
        self._thread = None

    @property
    def pending_requests(self):
        return self._pending_requests

    def start(self):
        self._server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server_socket.bind((self._host, self._port))
        self._server_socket.listen(5)
        self._server_socket.settimeout(0.5)
        self._thread = threading.Thread(target=self._accept_loop, name="BRQConsoleWorkerServer", daemon=True)
        self._thread.start()

    def stop(self):
        self._stop_event.set()
        if self._server_socket is not None:
            try:
                self._server_socket.close()
            except Exception:
                pass
        if self._thread is not None:
            self._thread.join(timeout=1.0)

    def _accept_loop(self):
        while not self._stop_event.is_set():
            try:
                client, _ = self._server_socket.accept()
            except socket.timeout:
                continue
            except OSError:
                break

            thread = threading.Thread(target=self._handle_client, args=(client,), daemon=True)
            thread.start()

    def _handle_client(self, client_socket):
        with client_socket:
            try:
                request = json.loads(self._recv_line(client_socket))
                if request.get("token") != self._token:
                    client_socket.sendall(
                        encode_message(
                            make_response(
                                request.get("request_id"),
                                False,
                                self._runtime.state.status,
                                error="Invalid worker token",
                            )
                        )
                    )
                    return

                pending = PendingRequest(request)
                self._pending_requests.put(pending)
                pending.event.wait(timeout=3600.0)

                response = pending.response or make_response(
                    request.get("request_id"),
                    False,
                    self._runtime.state.status,
                    error="Timed out waiting for Blender main thread",
                )
                client_socket.sendall(encode_message(response))
            except Exception as exc:
                client_socket.sendall(
                    encode_message(
                        make_response(None, False, self._runtime.state.status, error=str(exc))
                    )
                )

    @staticmethod
    def _recv_line(client_socket):
        buffer = bytearray()
        while True:
            chunk = client_socket.recv(4096)
            if not chunk:
                break
            buffer.extend(chunk)
            if b"\n" in chunk:
                break
        return buffer.decode("utf-8").strip()


class WorkerRuntime:
    def __init__(self, config):
        self._config = config
        endpoint = f"{config['host']}:{config['port']}"
        self.logger = Logger(config.get("log_path"))
        self.state = WorkerState(endpoint, config.get("app_instance_id", ""))
        self.server = WorkerServer(config["host"], int(config["port"]), config["token"], self)

    def start(self):
        self.server.start()
        self._register_handlers()
        self.state.refresh_from_context()
        self.state.set_status("ready")
        self.logger.write(f"Worker listening on {self.state.endpoint}")

        print("__BRQ_WORKER_READY__", flush=True)
        return self

    def _register_handlers(self):
        for handler_list, handler in (
            (bpy.app.handlers.load_pre, _on_load_pre),
            (bpy.app.handlers.load_post, _on_load_post),
        ):
            try:
                while handler in handler_list:
                    handler_list.remove(handler)
            except Exception:
                pass
            handler_list.append(handler)

    def process_pending_requests_once(self):
        while True:
            try:
                pending = self.server.pending_requests.get_nowait()
            except queue.Empty:
                break

            pending.response = handle_request(self, pending.request)
            pending.event.set()

    def run_forever(self):
        self.logger.write("Entering worker main loop")
        try:
            while not self.state.shutdown_requested:
                self.process_pending_requests_once()
                time.sleep(0.05)
        finally:
            self.stop()
            self._quit_blender()

    def on_load_pre(self):
        self.logger.write("Received load_pre")
        self.state.set_status("loading")

    def on_load_post(self):
        self.logger.write("Received load_post")
        self.state.refresh_from_context()
        if self.state.status != "rendering":
            self.state.set_status("ready")

    def request_shutdown(self):
        self.logger.write("Shutdown requested")
        self.state.shutdown_requested = True

    def stop(self):
        self.logger.write("Stopping worker server")
        self.server.stop()
        self.state.set_status("stopped")

    @staticmethod
    def _quit_blender():
        try:
            bpy.ops.wm.quit_blender()
        except Exception:
            pass
        return None


def handle_request(runtime, request):
    request_id = request.get("request_id")
    command = request.get("command")
    payload = request.get("payload") or {}

    try:
        runtime.state.request_count += 1
        runtime.state.touch_heartbeat()
        runtime.logger.write(f"Received command: {command}")

        if command == "ping":
            runtime.state.refresh_from_context()
            payload_out = runtime.state.snapshot_payload()
        elif command == "load_file":
            payload_out = load_file(runtime, payload.get("filepath", ""))
        elif command == "query_file_info":
            runtime.state.refresh_from_context()
            payload_out = runtime.state.snapshot_payload()
        elif command == "render_task":
            payload_out = render_task(runtime, payload)
        elif command == "cancel_current":
            payload_out = {
                "cancelled": False,
                "reason": "Use the desktop host to terminate and recover the worker process.",
            }
        elif command == "shutdown":
            runtime.request_shutdown()
            payload_out = {"scheduled": True}
        else:
            raise ValueError(f"Unknown worker command: {command}")

        return make_response(request_id, True, runtime.state.status, payload_out)
    except Exception as exc:
        runtime.state.set_status("error", str(exc))
        runtime.logger.write(f"Request failed: {exc}\n{traceback.format_exc()}")
        return make_response(request_id, False, runtime.state.status, error=str(exc))


def load_file(runtime, filepath):
    if not filepath:
        raise ValueError("load_file requires a filepath")

    runtime.state.set_status("loading")
    bpy.ops.wm.open_mainfile(filepath=filepath, load_ui=False, use_scripts=True)
    runtime.state.refresh_from_context()
    runtime.state.set_status("ready")
    return runtime.state.snapshot_payload()


def render_task(runtime, payload):
    scene_name = payload.get("scene_name")
    scene = bpy.data.scenes.get(scene_name) if scene_name else bpy.context.scene

    if scene is None:
        raise ValueError(f"Scene '{scene_name}' was not found")

    original_start = scene.frame_start
    original_end = scene.frame_end
    original_output = scene.render.filepath
    original_frame = scene.frame_current
    resolved_output_path = None

    try:
        frame_start = payload.get("frame_start")
        frame_end = payload.get("frame_end")
        output_path = payload.get("output_path")
        single_frame = payload.get("single_frame")
        runtime.logger.write(
            f"Render task started: scene={scene.name}, single_frame={single_frame}, frame_start={frame_start}, frame_end={frame_end}, output={output_path}"
        )

        if frame_start is not None:
            scene.frame_start = int(frame_start)
        if frame_end is not None:
            scene.frame_end = int(frame_end)
        if output_path:
            scene.render.filepath = output_path

        runtime.state.begin_render()

        if single_frame is not None:
            frame_number = int(single_frame)
            scene.frame_set(frame_number)
            bpy.ops.render.render(write_still=True, scene=scene.name)
            resolved_output_path = resolve_single_frame_output_path(scene, frame_number)
            runtime.state.set_output_verified(bool(resolved_output_path) and os.path.exists(resolved_output_path))
        else:
            bpy.ops.render.render(animation=True, scene=scene.name)
            runtime.state.set_output_verified(False)

        runtime.state.refresh_from_context()
        if resolved_output_path:
            runtime.state.output_path = resolved_output_path
        runtime.state.set_status("ready")
        runtime.logger.write("Render task finished")
        return runtime.state.snapshot_payload()
    finally:
        scene.frame_start = original_start
        scene.frame_end = original_end
        scene.render.filepath = original_output
        scene.frame_set(original_frame)


def resolve_single_frame_output_path(scene, frame_number):
    candidates = []

    try:
        candidates.append(bpy.path.abspath(scene.render.frame_path(frame=frame_number)))
    except Exception:
        pass

    try:
        base_path = bpy.path.abspath(scene.render.filepath)
        if base_path:
            candidates.append(base_path)
            file_extension = getattr(scene.render, "file_extension", "") or ""
            if file_extension and not base_path.endswith(file_extension):
                candidates.append(base_path + file_extension)
    except Exception:
        pass

    seen = set()
    for candidate in candidates:
        if not candidate or candidate in seen:
            continue
        seen.add(candidate)
        if os.path.exists(candidate):
            return candidate

    return candidates[0] if candidates else ""


@persistent
def _on_load_pre(_dummy):
    if BRQ_RUNTIME is not None:
        BRQ_RUNTIME.on_load_pre()


@persistent
def _on_load_post(_dummy):
    if BRQ_RUNTIME is not None:
        BRQ_RUNTIME.on_load_post()


def start_brq_worker(config):
    global BRQ_RUNTIME

    if BRQ_RUNTIME is not None:
        try:
            BRQ_RUNTIME.stop()
        except Exception:
            pass

    BRQ_RUNTIME = WorkerRuntime(config)
    BRQ_RUNTIME.start()
    return BRQ_RUNTIME


def run_brq_worker_forever(config):
    runtime = start_brq_worker(config)
    runtime.run_forever()
    return runtime
