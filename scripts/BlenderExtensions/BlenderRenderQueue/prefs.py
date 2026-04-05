import bpy

from bpy.props import BoolProperty, StringProperty
from bpy.types import AddonPreferences


class BlenderRenderQueuePreferences(AddonPreferences):
    bl_idname = __package__

    app_launch_path: StringProperty(
        name="App Launch Path",
        description="Optional path used when the sender tries to launch BlenderRenderQueue automatically",
        default="",
        subtype="FILE_PATH",
    )

    auto_start_app: BoolProperty(
        name="Auto Start BlenderRenderQueue",
        description="Try to start BlenderRenderQueue when submitting a task and the desktop app is not running",
        default=True,
    )

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True

        layout.prop(self, "auto_start_app")
        layout.prop(self, "app_launch_path")
