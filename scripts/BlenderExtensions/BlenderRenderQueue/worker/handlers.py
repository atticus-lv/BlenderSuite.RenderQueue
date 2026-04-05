from __future__ import annotations

import bpy
from bpy.app.handlers import persistent


def _runtime():
    from .bootstrap import get_runtime

    return get_runtime()


@persistent
def on_load_pre(dummy):
    runtime = _runtime()
    if runtime is not None:
        runtime.on_load_pre()


@persistent
def on_load_post(dummy):
    runtime = _runtime()
    if runtime is not None:
        runtime.on_load_post()


def register_handlers():
    if on_load_pre not in bpy.app.handlers.load_pre:
        bpy.app.handlers.load_pre.append(on_load_pre)

    if on_load_post not in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.append(on_load_post)


def unregister_handlers():
    if on_load_pre in bpy.app.handlers.load_pre:
        bpy.app.handlers.load_pre.remove(on_load_pre)

    if on_load_post in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(on_load_post)
