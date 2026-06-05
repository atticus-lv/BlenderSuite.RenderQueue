import bpy

from .operators import BSRQ_OT_submit_scene


def draw_header(self, context):
    if context.region.alignment != "RIGHT":
        return

    self.layout.operator(BSRQ_OT_submit_scene.bl_idname, text="Add", icon="EXPORT")


def register_panels():
    bpy.types.TOPBAR_HT_upper_bar.prepend(draw_header)


def unregister_panels():
    try:
        bpy.types.TOPBAR_HT_upper_bar.remove(draw_header)
    except Exception:
        pass
