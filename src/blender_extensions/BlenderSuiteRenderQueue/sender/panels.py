import bpy

from .operators import BSRQ_OT_submit_scene
from ..shared.icons import get_app_icon_value


def draw_header(self, context):
    if context.region.alignment != "RIGHT":
        return

    icon_value = get_app_icon_value()
    if icon_value:
        self.layout.operator(BSRQ_OT_submit_scene.bl_idname, text="Add", icon_value=icon_value)
    else:
        self.layout.operator(BSRQ_OT_submit_scene.bl_idname, text="Add", icon="ADD")


def register_panels():
    bpy.types.TOPBAR_HT_upper_bar.prepend(draw_header)


def unregister_panels():
    try:
        bpy.types.TOPBAR_HT_upper_bar.remove(draw_header)
    except Exception:
        pass
