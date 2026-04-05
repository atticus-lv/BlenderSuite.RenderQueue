from __future__ import annotations

import threading

import bpy

from ..shared.models import FileSessionSnapshot


class WorkerState:
    def __init__(self, extension_id: str, endpoint: str, app_instance_id: str):
        self.extension_id = extension_id
        self.endpoint = endpoint
        self.app_instance_id = app_instance_id
        self.status = "starting"
        self.last_error = ""
        self.request_count = 0
        self.shutdown_requested = False
        self._snapshot = FileSessionSnapshot()
        self._lock = threading.RLock()

    def set_status(self, status: str, error: str = "") -> None:
        with self._lock:
            self.status = status
            self.last_error = error

    def refresh_from_context(self) -> FileSessionSnapshot:
        with self._lock:
            scene = self._resolve_scene()
            filepath = self._safe_get_filepath()
            scene_names = self._safe_get_scene_names()
            camera = scene.camera.name if scene and getattr(scene, "camera", None) else ""
            active_scene = scene.name if scene else ""
            frame_start = scene.frame_start if scene else 0
            frame_end = scene.frame_end if scene else 0
            output_path = scene.render.filepath if scene else ""

            self._snapshot = FileSessionSnapshot(
                filepath=filepath,
                active_scene=active_scene,
                scenes=scene_names,
                camera=camera,
                frame_start=frame_start,
                frame_end=frame_end,
                output_path=output_path,
                is_saved=bool(filepath),
            )
            return self._snapshot

    def snapshot_payload(self) -> dict:
        with self._lock:
            file_snapshot = self._snapshot.to_payload()
            return {
                "extension_id": self.extension_id,
                "endpoint": self.endpoint,
                "app_instance_id": self.app_instance_id,
                "status": self.status,
                "last_error": self.last_error,
                "request_count": self.request_count,
                "file_session": file_snapshot,
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
    def _safe_get_filepath() -> str:
        try:
            return getattr(bpy.data, "filepath", "") or ""
        except Exception:
            return ""

    @staticmethod
    def _safe_get_scene_names() -> list[str]:
        try:
            return [scene.name for scene in getattr(bpy.data, "scenes", [])]
        except Exception:
            return []
