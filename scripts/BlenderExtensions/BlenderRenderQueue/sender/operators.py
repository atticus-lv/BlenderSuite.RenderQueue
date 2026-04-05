from __future__ import annotations

import os

import bpy
from bpy.props import BoolProperty, EnumProperty, IntProperty
from bpy.types import Operator

from .submit_service import submit_task


def enum_scene_items_callback(scene, context):
    scene_items = [(s.name, s.name, "") for s in bpy.data.scenes]
    scene_items.sort(key=lambda item: (item[0] != context.scene.name, item[0]))
    return scene_items


class BRQ_OT_submit_scene(Operator):
    bl_idname = "renderqueue.submit_scene"
    bl_label = "Submit Scene to Queue"
    bl_description = "Submit current scene to BlenderRenderQueue"
    bl_options = {"REGISTER", "UNDO"}

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

        try:
            response = submit_task(
                self.scene_name,
                self.override_frame_range,
                self.frame_start,
                self.frame_end,
                self.report,
            )

            if response.get("ok"):
                message = response.get("message") or f"Scene '{self.scene_name}' submitted to queue successfully."
                self.report({"INFO"}, message)
                return {"FINISHED"}

            message = response.get("message") or "Failed to submit scene."
            self.report({"ERROR"}, message)
            return {"CANCELLED"}
        except Exception as exc:
            self.report({"ERROR"}, f"Failed to submit scene: {exc}")
            return {"CANCELLED"}

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
