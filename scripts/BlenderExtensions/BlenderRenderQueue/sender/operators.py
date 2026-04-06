from __future__ import annotations

import os
import queue
import threading

import bpy
from bpy.props import BoolProperty, EnumProperty, IntProperty
from bpy.types import Operator

from .submit_service import _get_preferences, submit_task_payload


def enum_scene_items_callback(scene, context):
    scene_items = [(s.name, s.name, "") for s in bpy.data.scenes]
    scene_items.sort(key=lambda item: (item[0] != context.scene.name, item[0]))
    return scene_items


class BRQ_OT_submit_scene(Operator):
    bl_idname = "renderqueue.submit_scene"
    bl_label = "Submit Scene to Queue"
    bl_description = "Submit current scene to BlenderRenderQueue"
    bl_options = {"REGISTER", "UNDO"}

    _timer = None
    _worker_thread = None
    _result_queue = None

    scene_name: EnumProperty(name="Scene", items=enum_scene_items_callback)

    override_frame_range: BoolProperty(
        name="Custom",
        default=False,
    )

    frame_start: IntProperty(
        name="Start Frame",
        default=1,
        min=1,
    )

    frame_end: IntProperty(
        name="End Frame",
        default=250,
        min=1,
    )

    @classmethod
    def poll(cls, context):
        return bool(bpy.data.filepath)

    def execute(self, context):
        if not bpy.data.filepath:
            self.report({"ERROR"}, "Save the .blend file before submitting it to BlenderRenderQueue.")
            return {"CANCELLED"}

        prefs = _get_preferences()
        auto_start_app = bool(prefs and prefs.auto_start_app)
        app_launch_path = prefs.app_launch_path if prefs else ""
        auto_start_queue = bool(prefs and prefs.auto_start_queue_after_submit)

        self._result_queue = queue.Queue()
        self._worker_thread = threading.Thread(
            target=self._submit_worker,
            kwargs={
                "result_queue": self._result_queue,
                "blend_file_path": bpy.data.filepath,
                "scene_name": self.scene_name,
                "override_frame_range": self.override_frame_range,
                "frame_start": self.frame_start,
                "frame_end": self.frame_end,
                "auto_start_app": auto_start_app,
                "app_launch_path": app_launch_path,
                "auto_start_queue": auto_start_queue,
            },
            daemon=True,
        )
        self._worker_thread.start()

        window_manager = context.window_manager
        self._timer = window_manager.event_timer_add(0.2, window=context.window)
        window_manager.modal_handler_add(self)
        self.report({"INFO"}, "Submitting scene to BlenderRenderQueue...")
        return {"RUNNING_MODAL"}

    def modal(self, context, event):
        if event.type != "TIMER" or self._result_queue is None:
            return {"PASS_THROUGH"}

        try:
            status, payload = self._result_queue.get_nowait()
        except queue.Empty:
            if self._worker_thread is not None and self._worker_thread.is_alive():
                return {"PASS_THROUGH"}

            self._finish_modal(context)
            self.report({"ERROR"}, "Failed to submit scene: unknown background error.")
            return {"CANCELLED"}

        self._finish_modal(context)

        if status == "ok":
            message = payload.get("message") or f"Scene '{self.scene_name}' submitted to queue successfully."
            self.report({"INFO"}, message)
            return {"FINISHED"}

        self.report({"ERROR"}, f"Failed to submit scene: {payload}")
        return {"CANCELLED"}

    def cancel(self, context):
        self._finish_modal(context)

    def _finish_modal(self, context):
        if self._timer is not None:
            context.window_manager.event_timer_remove(self._timer)
            self._timer = None
        self._worker_thread = None
        self._result_queue = None

    def _submit_worker(
        self,
        *,
        result_queue,
        blend_file_path: str,
        scene_name: str,
        override_frame_range: bool,
        frame_start: int,
        frame_end: int,
        auto_start_app: bool,
        app_launch_path: str,
        auto_start_queue: bool,
    ):
        try:
            response = submit_task_payload(
                blend_file_path,
                scene_name,
                override_frame_range,
                frame_start,
                frame_end,
                auto_start_app=auto_start_app,
                app_launch_path=app_launch_path,
                auto_start_queue=auto_start_queue,
            )
            result_queue.put(("ok", response))
        except Exception as exc:
            result_queue.put(("error", str(exc)))

    def invoke(self, context, event):
        scene = context.scene
        self.frame_start = scene.frame_start
        self.frame_end = scene.frame_end
        self.scene_name = scene.name
        return context.window_manager.invoke_props_dialog(self, width=400)

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        box = layout.box()
        box.label(text=os.path.basename(bpy.data.filepath), icon="FILE_BLEND")
        box.label(text=self.scene_name, icon="SCENE_DATA")
        if not self.override_frame_range:
            scene = bpy.data.scenes[self.scene_name]
            box.label(text=f"{scene.frame_start}-{scene.frame_end}", icon="PREVIEW_RANGE")
        else:
            box.label(text=f"{self.frame_start}-{self.frame_end}", icon="PREVIEW_RANGE")

        layout.prop(self, "scene_name", icon="SCENE_DATA")
        layout.prop(self, "override_frame_range")

        if self.override_frame_range:
            row = layout.row()
            row.prop(self, "frame_start")
            row.prop(self, "frame_end")
