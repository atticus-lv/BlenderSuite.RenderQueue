import bpy

from .prefs import BlenderRenderQueuePreferences
from .sender.operators import BRQ_OT_submit_scene
from .sender.panels import draw_header, register_panels, unregister_panels
from .worker.bootstrap import maybe_start_worker, stop_worker
from .worker.handlers import register_handlers, unregister_handlers


classes = (
    BlenderRenderQueuePreferences,
    BRQ_OT_submit_scene,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)

    register_panels()
    register_handlers()
    maybe_start_worker()


def unregister():
    stop_worker()
    unregister_handlers()
    unregister_panels()

    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
