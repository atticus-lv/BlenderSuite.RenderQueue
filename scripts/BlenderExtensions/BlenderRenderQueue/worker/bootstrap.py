from __future__ import annotations

import queue

import bpy

from ..shared.logging import Logger
from .activation import get_activation_context
from .commands import handle_request
from .server import WorkerServer
from .state import WorkerState


_runtime = None


class WorkerRuntime:
    def __init__(self, context):
        endpoint = f"{context.host}:{context.port}"
        self.logger = Logger(context.log_path)
        self.state = WorkerState("BlenderRenderQueue", endpoint, context.app_instance_id)
        self.server = WorkerServer(context.host, context.port, context.token, self)
        self._timer_registered = False

    def start(self):
        self.server.start()
        self.state.refresh_from_context()
        self.state.set_status("ready")
        self.logger.write(f"Worker listening on {self.state.endpoint}")

        if not self._timer_registered:
            bpy.app.timers.register(self.process_pending_requests, first_interval=0.1, persistent=True)
            self._timer_registered = True

    def process_pending_requests(self):
        while True:
            try:
                pending = self.server.pending_requests.get_nowait()
            except queue.Empty:
                break

            pending.response = handle_request(self, pending.request)
            pending.event.set()

        if self.state.shutdown_requested:
            self.stop()
            bpy.app.timers.register(self._quit_blender, first_interval=0.1)
            return None

        return 0.1

    def on_load_pre(self):
        self.logger.write("Received load_pre")
        self.state.set_status("loading")

    def on_load_post(self):
        self.logger.write("Received load_post")
        self.state.refresh_from_context()
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


def maybe_start_worker():
    global _runtime

    if _runtime is not None:
        return _runtime

    context = get_activation_context()
    if context is None:
        return None

    _runtime = WorkerRuntime(context)
    _runtime.start()
    return _runtime


def stop_worker():
    global _runtime

    if _runtime is None:
        return

    _runtime.stop()
    _runtime = None


def get_runtime():
    return _runtime
