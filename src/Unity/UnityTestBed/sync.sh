#!/usr/bin/env bash
# Copies the test sources and the TestMore scripts into the Unity test bed. The MoonSharp
# package itself comes from tools/upm/stage-local-package.sh (see Packages/manifest.json).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TESTS="${HERE}/../../MoonSharp.Interpreter.Tests"
SOURCES_DEST="${HERE}/Assets/MoonSharp.Interpreter.Tests"
SCRIPTS_DEST="${HERE}/Assets/Resources/MoonSharp/Scripts"

# Replace the mirrored C# files; the committed asmdef and Unity's .meta files stay.
find "${SOURCES_DEST}" -name '*.cs' -delete
(cd "${TESTS}" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' -print0 \
  | xargs -0 cp --parents -t "${SOURCES_DEST}")

# Unity loads text assets by file name without the last extension, so 000-sanity.t.txt is
# found as "000-sanity.t" and More.lua.txt as "More.lua", the names the tests ask for.
rm -rf "${SCRIPTS_DEST}"
mkdir -p "${SCRIPTS_DEST}"
for f in "${TESTS}"/TestMore/*.t "${TESTS}"/TestMore/Modules/Test/*.lua; do
  cp "${f}" "${SCRIPTS_DEST}/$(basename "${f}").txt"
done

printf 'UnityTestBed: %d C# files, %d scripts.\n' \
  "$(find "${SOURCES_DEST}" -name '*.cs' | wc -l)" "$(find "${SCRIPTS_DEST}" -name '*.txt' | wc -l)"
