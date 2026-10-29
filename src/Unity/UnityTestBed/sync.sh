#!/usr/bin/env bash
# Copies the test sources, the TestMore scripts and the benchmark workloads into the Unity test
# bed. The MoonSharp package itself comes from tools/upm/stage-local-package.sh (see
# Packages/manifest.json).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TESTS="${HERE}/../../MoonSharp.Interpreter.Tests"
SOURCES_DEST="${HERE}/Assets/MoonSharp.Interpreter.Tests"
SCRIPTS_DEST="${HERE}/Assets/Resources/MoonSharp/Scripts"
BENCH="${HERE}/../../DevTools/Benchmarks"
BENCH_DEST="${HERE}/Assets/Benchmarks"

# Replace the mirrored C# files; the committed asmdef and Unity's .meta files stay.
find "${SOURCES_DEST}" -name '*.cs' -delete
(cd "${TESTS}" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' -print0 \
  | xargs -0 cp --parents -t "${SOURCES_DEST}")

# Unity loads text assets by file name without the last extension, so 000-sanity.t.txt is
# found as "000-sanity.t" and More.lua.txt as "More.lua", the names the tests ask for.
rm -rf "${SCRIPTS_DEST}"
mkdir -p "${SCRIPTS_DEST}"
for f in "${TESTS}"/TestMore/*.t "${TESTS}"/TestMore/Modules/Test/*.lua "${TESTS}"/Lua52Differential/cases/*.lua; do
  cp "${f}" "${SCRIPTS_DEST}/$(basename "${f}").txt"
done

# The workloads and their runner for -moonsharpBench, compiled into Assembly-CSharp. The rest of
# the benchmark project needs BenchmarkDotNet and .NET-only APIs.
rm -rf "${BENCH_DEST}"
mkdir -p "${BENCH_DEST}"
cp "${BENCH}/LuaWorkloads.cs" "${BENCH}/LuaCSharpScripts.cs" "${BENCH}/WorkloadTsv.cs" "${BENCH_DEST}/"

printf 'UnityTestBed: %d C# files, %d scripts, %d benchmark files.\n' \
  "$(find "${SOURCES_DEST}" -name '*.cs' | wc -l)" "$(find "${SCRIPTS_DEST}" -name '*.txt' | wc -l)" \
  "$(find "${BENCH_DEST}" -name '*.cs' | wc -l)"
