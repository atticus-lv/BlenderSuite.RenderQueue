from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class FileSessionSnapshot:
    filepath: str = ""
    active_scene: str = ""
    scenes: list[str] = field(default_factory=list)
    camera: str = ""
    frame_start: int = 0
    frame_end: int = 0
    output_path: str = ""
    is_saved: bool = False

    def to_payload(self) -> dict[str, Any]:
        return {
            "filepath": self.filepath,
            "active_scene": self.active_scene,
            "scenes": list(self.scenes),
            "camera": self.camera,
            "frame_start": self.frame_start,
            "frame_end": self.frame_end,
            "output_path": self.output_path,
            "is_saved": self.is_saved,
        }
