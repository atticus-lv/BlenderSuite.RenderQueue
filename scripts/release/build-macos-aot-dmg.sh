#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: build-macos-aot-dmg.sh [osx-arm64|osx-x64] [--install] [--no-open]

Builds a macOS Native AOT publish, wraps it in a .app bundle, and creates a .dmg.
If no RID is provided, the current machine architecture is used.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/build-macos-dmg.sh" --aot "$@"
