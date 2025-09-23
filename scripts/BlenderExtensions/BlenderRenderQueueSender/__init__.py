import bpy
import os
import json

from pathlib import Path

from bpy.types import AddonPreferences, Operator, Panel
from bpy.props import StringProperty, BoolProperty, IntProperty


class RenderQueuePreferences(AddonPreferences):
    """偏好设置面板"""
    bl_idname = __name__

    def draw(self, context):
        layout = self.layout


class RENDERQUEUE_OT_submit_scene(Operator):
    """提交当前场景到渲染队列"""
    bl_idname = "renderqueue.submit_scene"
    bl_label = "Submit Scene to Queue"
    bl_description = "Submit current scene to BlenderRenderQueue"
    bl_options = {'REGISTER', 'UNDO'}

    override_frame_range: BoolProperty(
        name="Override Frame Range",
        description="Override scene frame range",
        default=False
    )

    start_frame: IntProperty(
        name="Start Frame",
        description="Start frame for rendering",
        default=1,
        min=1
    )

    end_frame: IntProperty(
        name="End Frame",
        description="End frame for rendering",
        default=250,
        min=1
    )

    @classmethod
    def poll(cls, context):
        return bpy.data.filepath

    def execute(self, context):
        # 获取偏好设置

        app_dir = Path.home().joinpath("AppData", "Roaming", "BlenderRenderQueue")
        app_dir.mkdir(parents=True, exist_ok=True)

        # 获取data_from_blender.json路径
        data_json_path = app_dir.joinpath("data_from_blender.json")

        # 获取当前场景信息
        scene = context.scene
        blend_file_path = bpy.data.filepath
        blend_filename = os.path.basename(blend_file_path)
        current_scene_name = scene.name

        # 构建新的渲染任务
        new_task = {
            "RenderTask": {
                "Filename": blend_filename,
                "Filepath": blend_file_path,
                "StartFrame": scene.frame_start,
                "EndFrame": scene.frame_end,
                "LastRenderedFrame": 0,
                "Enable": True
            }
        }

        # 如果启用了帧范围覆写
        if self.override_frame_range:
            new_task["RenderTask"]["Override"] = {
                "OverrideFrameRange": {
                    "StartFrame": self.start_frame,
                    "EndFrame": self.end_frame
                }
            }
        else:
            # 添加场景覆写信息 - 使用正确的数据结构
            new_task["RenderTask"]["Override"] = {
                "OverrideScene": {
                    "SceneName": current_scene_name
                }
            }

        try:
            # 直接创建完整的JSON文件，不读取现有文件
            data = {
                "Software": "BlenderRenderQueue",
                "Version": "0.0.1",
                "Settings": {
                    "BlenderPath": "",
                    "FfmpegPath": ""
                },
                "RenderQueue": [new_task]
            }

            # 写入文件
            with open(data_json_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2, ensure_ascii=False)

            self.report({'INFO'}, f"Scene '{current_scene_name}' submitted to queue successfully!")
            return {'FINISHED'}

        except Exception as e:
            self.report({'ERROR'}, f"Failed to submit scene: {str(e)}")
            return {'CANCELLED'}


def invoke(self, context, event):
    # 设置默认帧范围
    scene = context.scene
    self.start_frame = scene.frame_start
    self.end_frame = scene.frame_end

    return context.window_manager.invoke_props_dialog(self, width=400)


def draw(self, context):
    layout = self.layout

    # 显示当前场景信息
    scene = context.scene
    layout.label(text=f"{scene.name}", icon='SCENE_DATA')
    layout.label(text=f"{os.path.basename(bpy.data.filepath) if bpy.data.filepath else 'Unsaved'}",
                 icon='FILE_BLEND')

    layout.separator()

    # 帧范围覆写选项
    layout.prop(self, "override_frame_range")

    if self.override_frame_range:
        col = layout.column()
        col.prop(self, "start_frame")
        col.prop(self, "end_frame")
    else:
        layout.label(text="Will use current scene with frame range override")


class RENDERQUEUE_PT_panel(Panel):
    """渲染队列面板"""
    bl_label = "Sumit Render"
    bl_idname = "RENDERQUEUE_PT_panel"
    bl_space_type = 'PROPERTIES'
    bl_region_type = 'WINDOW'
    bl_context = 'output'
    bl_category = "RenderQueue"

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True

        scene = context.scene
        blend_file_path = bpy.data.filepath

        if not blend_file_path:
            layout.label(text="Please save the blend file first", icon='ERROR')
            return

        box = layout.box()
        box.label(text=f"{scene.name}", icon='SCENE_DATA')
        box.label(text=f"{os.path.basename(blend_file_path)}", icon='FILE_BLEND')
        box.label(text=f"{scene.frame_start}-{scene.frame_end}", icon="PREVIEW_RANGE")

        layout.separator()

        layout.operator("renderqueue.submit_scene", text="Submit to Queue", icon='EXPORT')


def register():
    bpy.utils.register_class(RenderQueuePreferences)
    bpy.utils.register_class(RENDERQUEUE_OT_submit_scene)
    bpy.utils.register_class(RENDERQUEUE_PT_panel)


def unregister():
    bpy.utils.unregister_class(RENDERQUEUE_PT_panel)
    bpy.utils.unregister_class(RENDERQUEUE_OT_submit_scene)
    bpy.utils.unregister_class(RenderQueuePreferences)


if __name__ == "__main__":
    register()
