from __future__ import annotations

import os
import threading
from datetime import datetime


class Logger:
    def __init__(self, log_path: str | None):
        self._log_path = log_path
        self._lock = threading.Lock()

    def write(self, message: str) -> None:
        line = f"[BRQ][{datetime.utcnow().isoformat()}] {message}"
        print(line)

        if not self._log_path:
            return

        try:
            os.makedirs(os.path.dirname(self._log_path), exist_ok=True)
            with self._lock:
                with open(self._log_path, "a", encoding="utf-8") as handle:
                    handle.write(line + "\n")
        except Exception:
            pass
