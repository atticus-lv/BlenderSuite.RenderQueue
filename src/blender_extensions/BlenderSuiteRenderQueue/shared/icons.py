from __future__ import annotations

from pathlib import Path

import bpy.utils.previews


_preview_collections = {}
_APP_ICON_KEY = "app_icon"


def register_icons():
    previews = bpy.utils.previews.new()
    icon_path = Path(__file__).resolve().parents[1] / "assets" / "app_icon.png"
    previews.load(_APP_ICON_KEY, str(icon_path), "IMAGE")
    _preview_collections["main"] = previews


def unregister_icons():
    for previews in _preview_collections.values():
        bpy.utils.previews.remove(previews)
    _preview_collections.clear()


def get_app_icon_value():
    previews = _preview_collections.get("main")
    if previews is None:
        return 0

    icon = previews.get(_APP_ICON_KEY)
    return icon.icon_id if icon is not None else 0
