#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "MoonSharp: syncing source mirrors..."
"${SRC_ROOT}/rsync_unity_projects.sh"

echo "MoonSharp: syncing the UnityTestBed test sources and scripts..."
"${SCRIPT_DIR}/UnityTestBed/sync.sh"
