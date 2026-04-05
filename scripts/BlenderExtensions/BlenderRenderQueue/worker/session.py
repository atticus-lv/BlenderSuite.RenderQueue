from __future__ import annotations

import bpy

from .state import WorkerState


def load_file(state: WorkerState, filepath: str) -> dict:
    if not filepath:
        raise ValueError("load_file requires a filepath")

    state.set_status("loading")
    bpy.ops.wm.open_mainfile(filepath=filepath, load_ui=False, use_scripts=True)
    state.set_status("ready")
    state.refresh_from_context()
    return state.snapshot_payload()


def query_file_info(state: WorkerState) -> dict:
    state.refresh_from_context()
    return state.snapshot_payload()


def render_task(state: WorkerState, payload: dict) -> dict:
    scene_name = payload.get("scene_name")
    if scene_name:
        scene = bpy.data.scenes.get(scene_name)
    else:
        scene = bpy.context.scene

    if scene is None:
        raise ValueError(f"Scene '{scene_name}' was not found")

    original_start = scene.frame_start
    original_end = scene.frame_end
    original_output = scene.render.filepath
    original_frame = scene.frame_current

    try:
        frame_start = payload.get("frame_start")
        frame_end = payload.get("frame_end")
        output_path = payload.get("output_path")
        single_frame = payload.get("single_frame")

        if frame_start is not None:
            scene.frame_start = int(frame_start)
        if frame_end is not None:
            scene.frame_end = int(frame_end)
        if output_path:
            scene.render.filepath = output_path

        state.set_status("rendering")

        if single_frame is not None:
            frame_number = int(single_frame)
            scene.frame_set(frame_number)
            bpy.ops.render.render(write_still=True, scene=scene.name)
        else:
            bpy.ops.render.render(animation=True, scene=scene.name)

        state.set_status("ready")
        state.refresh_from_context()
        return state.snapshot_payload()
    finally:
        scene.frame_start = original_start
        scene.frame_end = original_end
        scene.render.filepath = original_output
        scene.frame_set(original_frame)


def cancel_current(state: WorkerState) -> dict:
    if state.status != "rendering":
        return {
            "cancelled": False,
            "reason": "Worker is not currently rendering",
        }

    raise RuntimeError("Cancelling renders from the worker extension is not implemented yet")
