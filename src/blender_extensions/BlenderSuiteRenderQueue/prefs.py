import bpy

from bpy.props import BoolProperty, StringProperty
from bpy.types import AddonPreferences


class BlenderSuiteRenderQueuePreferences(AddonPreferences):
    bl_idname = __package__

    app_launch_path: StringProperty(
        name="App Launch Path",
        description="Optional path used when the sender tries to launch Blender Suite Render Queue automatically",
        default="",
        subtype="FILE_PATH",
    )

    auto_start_app: BoolProperty(
        name="Auto Start Blender Suite Render Queue",
        description="Try to start Blender Suite Render Queue when submitting a task and the desktop app is not running",
        default=True,
    )

    auto_start_queue_after_submit: BoolProperty(
        name="Auto Start Queue After Submit",
        description="Start the desktop render queue immediately after this extension submits a task",
        default=False,
    )

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True

        layout.prop(self, "auto_start_app")
        layout.prop(self, "auto_start_queue_after_submit")
        layout.prop(self, "app_launch_path")
