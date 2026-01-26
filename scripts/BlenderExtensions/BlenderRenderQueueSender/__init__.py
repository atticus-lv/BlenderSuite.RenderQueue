import bpy
import os
import json
import subprocess
import time

from pathlib import Path
from shutil import which

from bpy.types import AddonPreferences, Operator, Panel
from bpy.props import StringProperty, BoolProperty, IntProperty, EnumProperty


class RenderQueuePreferences(AddonPreferences):
    """偏好设置面板"""
    bl_idname = __name__

    def draw(self, context):
        layout = self.layout


SCENE_ITEMS = []


def _is_blender_render_queue_running(process_name: str = "BlenderRenderQueue.exe") -> bool:
    try:
        create_no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        output = subprocess.check_output(
            ["tasklist"],
            creationflags=create_no_window,
        ).decode(errors="ignore")
        return process_name.lower() in output.lower()
    except Exception:
        return False


def _find_blender_render_queue_exe() -> str | None:
    exe_path = which("BlenderRenderQueue.exe")
    if exe_path and os.path.isfile(exe_path):
        return exe_path

    candidates = []
    for env_var in ("ProgramFiles", "ProgramFiles(x86)"):
        base = os.environ.get(env_var)
        if base:
            candidates.append(os.path.join(base, "BlenderRenderQueue", "BlenderRenderQueue.exe"))

    addon_dir = os.path.dirname(__file__)
    candidates.append(os.path.join(addon_dir, "BlenderRenderQueue.exe"))

    for candidate in candidates:
        if os.path.isfile(candidate):
            return candidate

    return None


def _ensure_blender_render_queue_started(self_reporter) -> bool:
    """
    Make sure BlenderRenderQueue is started before committing the render:
    - If it is not running, try starting it via cmd /c start
    - Any errors are only prompted as WARNINGs and do not disrupt the subsequent submission process
    """
    if _is_blender_render_queue_running():
        return False

    exe_path = _find_blender_render_queue_exe()
    if not exe_path:
        try:
            self_reporter({'WARNING'}, "未找到 BlenderRenderQueue.exe，已写入队列数据，但请手动启动 BlenderRenderQueue。")
        except Exception:
            pass
        return False

    try:
        create_no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        subprocess.Popen(
            f'cmd /c start "" "{exe_path}"',
            shell=True,
            creationflags=create_no_window,
        )
        return True
    except Exception as e:
        try:
            self_reporter({'WARNING'}, f"尝试启动 BlenderRenderQueue 失败：{e}。请手动启动后再使用队列。")
        except Exception:
            pass
        return False


def enum_scene_items_callback(scene, context):
    scene_items = [(s.name, s.name, "") for s in bpy.data.scenes]
    scene_items.sort(key=lambda x: (x[0] != context.scene.name, x[0]))  # sort, the current scene is first
    global SCENE_ITEMS
    SCENE_ITEMS = scene_items
    return scene_items


class RENDERQUEUE_OT_submit_scene(Operator):
    """提交当前场景到渲染队列"""
    bl_idname = "renderqueue.submit_scene"
    bl_label = "Submit Scene to Queue"
    bl_description = "Submit current scene to BlenderRenderQueue"
    bl_options = {'REGISTER', 'UNDO'}

    scene_name: EnumProperty(name="Scene", items=enum_scene_items_callback)

    override_frame_range: BoolProperty(
        name="Custom",
        default=False
    )

    frame_start: IntProperty(
        name="Start Frame",
        default=1,
        min=1
    )

    frame_end: IntProperty(
        name="End Frame",
        default=250,
        min=1
    )

    @classmethod
    def poll(cls, context):
        return bpy.data.filepath

    def execute(self, context):
        app_dir = Path.home().joinpath("AppData", "Roaming", "BlenderRenderQueue")
        app_dir.mkdir(parents=True, exist_ok=True)

        data_json_path = app_dir.joinpath("data_from_blender.json")

        started_now = _ensure_blender_render_queue_started(self.report)
        if started_now:
            for _ in range(20):  # up to ~5s
                if _is_blender_render_queue_running():
                    break
                time.sleep(0.25)
            time.sleep(1.0)  # give UI + watcher extra time

        scene = context.scene
        blend_file_path = bpy.data.filepath
        blend_filename = os.path.basename(blend_file_path)

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

        if self.override_frame_range:
            new_task["RenderTask"]["Override"] = {
                "OverrideFrameRange": {
                    "StartFrame": self.frame_start,
                    "EndFrame": self.frame_end
                },
                "OverrideScene": {
                    "SceneName": self.scene_name
                }
            }
        else:
            new_task["RenderTask"]["Override"] = {
                "OverrideScene": {
                    "SceneName": self.scene_name
                }
            }

        try:
            data = {
                "Software": "BlenderRenderQueue",
                "Version": "0.0.1",
                "RenderQueue": [new_task]
            }

            # 写入文件
            with open(data_json_path, 'w', encoding='utf-8') as f:
                # clean up the json file and write new data
                json.dump(data, f, indent=2, ensure_ascii=False)

            if started_now:
                time.sleep(0.75)
                try:
                    os.utime(data_json_path, None)
                except Exception:
                    pass

            self.report({'INFO'}, f"Scene '{self.scene_name}' submitted to queue successfully!")
            return {'FINISHED'}

        except Exception as e:
            self.report({'ERROR'}, f"Failed to submit scene: {str(e)}")
            return {'CANCELLED'}

    def invoke(self, context, event):
        # 设置默认帧范围
        scene = context.scene
        self.frame_start = scene.frame_start
        self.frame_end = scene.frame_end

        return context.window_manager.invoke_props_dialog(self, width=400, )

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        box = layout.box()
        box.label(text=f"{os.path.basename(bpy.data.filepath)}", icon='FILE_BLEND')
        box.label(text=self.scene_name, icon='SCENE_DATA')
        if not self.override_frame_range:
            scene = bpy.data.scenes[self.scene_name]
            box.label(text=f"{scene.frame_start}-{scene.frame_end}", icon="PREVIEW_RANGE")
        else:
            box.label(text=f"{self.frame_start}-{self.frame_end}", icon="PREVIEW_RANGE")

        layout.prop(self, "scene_name", icon='SCENE_DATA')
        layout.prop(self, "override_frame_range")

        if self.override_frame_range:
            row = layout.row()
            row.prop(self, "frame_start")
            row.prop(self, "frame_end")


iconspreview_collections = {}
icon_name = "SEND"


def draw_header(self, context):
    if context.region.alignment != 'RIGHT':
        return
    layout = self.layout
    layout.operator(RENDERQUEUE_OT_submit_scene.bl_idname, text="Add",
                    icon_value=iconspreview_collections["main"][icon_name].icon_id)


def register():
    icons = bpy.utils.previews.new()
    icons_dir = os.path.join(os.path.dirname(__file__), "icon")
    icons.load(icon_name, os.path.join(icons_dir, "logo.png"), 'IMAGE')
    iconspreview_collections["main"] = icons

    bpy.types.TOPBAR_HT_upper_bar.prepend(draw_header)
    bpy.utils.register_class(RenderQueuePreferences)
    bpy.utils.register_class(RENDERQUEUE_OT_submit_scene)


def unregister():
    bpy.types.TOPBAR_HT_upper_bar.remove(draw_header)
    bpy.utils.unregister_class(RENDERQUEUE_OT_submit_scene)
    bpy.utils.unregister_class(RenderQueuePreferences)


if __name__ == "__main__":
    register()
