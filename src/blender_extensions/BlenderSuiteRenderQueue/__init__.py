import bpy

from .prefs import BlenderSuiteRenderQueuePreferences
from .sender.operators import BSRQ_OT_submit_scene
from .sender.panels import draw_header, register_panels, unregister_panels


classes = (
    BlenderSuiteRenderQueuePreferences,
    BSRQ_OT_submit_scene,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)

    register_panels()


def unregister():
    unregister_panels()

    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
