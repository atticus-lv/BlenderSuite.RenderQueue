from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from pathlib import Path
from shutil import which

import bpy

from ..shared.paths import get_submission_file_path


def _get_preferences():
    addon = bpy.context.preferences.addons.get(__package__.split(".")[0])
    return addon.preferences if addon else None


def _find_app_launch_target() -> str | None:
    prefs = _get_preferences()
    if prefs and prefs.app_launch_path and os.path.exists(prefs.app_launch_path):
        return prefs.app_launch_path

    explicit_path = os.environ.get("BRQ_APP_PATH")
    if explicit_path and os.path.exists(explicit_path):
        return explicit_path

    executable_names = ["BlenderRenderQueue.exe", "BlenderRenderQueue"]
    for executable_name in executable_names:
        found = which(executable_name)
        if found and os.path.isfile(found):
            return found

    if sys.platform == "win32":
        for env_var in ("ProgramFiles", "ProgramFiles(x86)"):
            base = os.environ.get(env_var)
            if base:
                candidate = Path(base) / "BlenderRenderQueue" / "BlenderRenderQueue.exe"
                if candidate.is_file():
                    return str(candidate)
    elif sys.platform == "darwin":
        for candidate in (
            Path("/Applications/BlenderRenderQueue.app"),
            Path.home() / "Applications" / "BlenderRenderQueue.app",
        ):
            if candidate.exists():
                return str(candidate)

    return None


def _is_app_running() -> bool:
    try:
        if sys.platform == "win32":
            create_no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            output = subprocess.check_output(["tasklist"], creationflags=create_no_window).decode(errors="ignore")
            return "blenderrenderqueue.exe" in output.lower()

        result = subprocess.run(
            ["pgrep", "-f", "BlenderRenderQueue"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return result.returncode == 0
    except Exception:
        return False


def ensure_app_started(report_callback) -> bool:
    prefs = _get_preferences()
    if prefs and not prefs.auto_start_app:
        return False

    if _is_app_running():
        return False

    launch_target = _find_app_launch_target()
    if not launch_target:
        report_callback(
            {"WARNING"},
            "BlenderRenderQueue is not running. Task data was written, but you may need to start the desktop app manually.",
        )
        return False

    try:
        if sys.platform == "win32":
            create_no_window = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            subprocess.Popen(
                f'cmd /c start "" "{launch_target}"',
                shell=True,
                creationflags=create_no_window,
            )
        elif launch_target.endswith(".app"):
            subprocess.Popen(["open", "-a", launch_target])
        else:
            subprocess.Popen([launch_target])

        return True
    except Exception as exc:
        report_callback({"WARNING"}, f"Failed to start BlenderRenderQueue automatically: {exc}")
        return False


def write_submission(
    scene_name: str,
    override_frame_range: bool,
    frame_start: int,
    frame_end: int,
    started_now: bool = False,
):
    submission_file = get_submission_file_path()

    if started_now:
        for _ in range(20):
            if _is_app_running():
                break
            time.sleep(0.25)
        time.sleep(0.75)

    scene = bpy.context.scene
    blend_file_path = bpy.data.filepath
    blend_filename = os.path.basename(blend_file_path)

    new_task = {
        "RenderTask": {
            "Filename": blend_filename,
            "Filepath": blend_file_path,
            "StartFrame": scene.frame_start,
            "EndFrame": scene.frame_end,
            "LastRenderedFrame": 0,
            "Enable": True,
        }
    }

    override_payload = {
        "OverrideScene": {
            "SceneName": scene_name,
        }
    }

    if override_frame_range:
        override_payload["OverrideFrameRange"] = {
            "StartFrame": frame_start,
            "EndFrame": frame_end,
        }

    new_task["RenderTask"]["Override"] = override_payload

    data = {
        "Software": "BlenderRenderQueue",
        "Version": "0.0.1",
        "RenderQueue": [new_task],
    }

    with open(submission_file, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)

    if started_now:
        time.sleep(0.5)
        os.utime(submission_file, None)
