from __future__ import annotations

import os
import sys
from pathlib import Path


def get_app_data_dir() -> Path:
    explicit_dir = os.environ.get("BSRQ_APP_DATA_DIR")
    if explicit_dir:
        return Path(explicit_dir)

    home = Path.home()
    if sys.platform == "win32":
        return Path(os.environ.get("APPDATA", home / "AppData" / "Roaming")) / "BlenderSuite.RenderQueue"
    if sys.platform == "darwin":
        return home / "Library" / "Application Support" / "BlenderSuite.RenderQueue"
    return Path(os.environ.get("XDG_CONFIG_HOME", home / ".config")) / "BlenderSuite.RenderQueue"


def get_submission_endpoint_path() -> Path:
    app_data_dir = get_app_data_dir()
    app_data_dir.mkdir(parents=True, exist_ok=True)
    return app_data_dir / "submission_endpoint.json"
