#!/usr/bin/env bash
# Runs the unit suite compiled with NativeAOT, a stand-in for IL2CPP until Unity CI runs.
# Fails when a test fails that nativeaot-known-failures.txt does not list, when a listed test
# passes (remove it from the list), or when the AOT build runs fewer tests than the JIT build.
#
#   src/TestRunners/DotNetCoreTestRunner/nativeaot.sh [output directory]
#
# Needs the .NET SDK, clang and zlib1g-dev. Linux x64.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$(mkdir -p "${1:-$here/bin/nativeaot}" && cd "${1:-$here/bin/nativeaot}" && pwd)"

# Prints the names of the failed tests in a runner log.
failed_tests() {
  awk '/^[A-Za-z_][A-Za-z0-9_]* - / && !/ - (ok|skipped.*)$/ { sub(/ - .*/, ""); print }' "$1" | sort -u
}

# Prints how many tests the runner found: run ones plus skipped ones.
discovered_tests() {
  sed -n -E 's/^OK : [0-9]+\/([0-9]+), Failed [0-9]+\/[0-9]+, Skipped ([0-9]+)\/[0-9]+$/\1 \2/p' "$1" | awk '{print $1 + $2}'
}

# The build logs carry the trim and AOT analysis warnings.
dotnet build "$here" -c Release -nologo > "$out/build.log" 2>&1 || { tail -n 50 "$out/build.log"; exit 1; }
dotnet publish "$here" -c Release -r linux-x64 -p:NativeAot=true -o "$out" -nologo > "$out/publish.log" 2>&1 || { tail -n 50 "$out/publish.log"; exit 1; }

# The runner reads TestMore/ relative to the working directory. Its exit code is the number of
# failed tests (999 when it crashes).
set +e
(cd "$here/bin/Release/net8.0" && timeout 600 dotnet DotNetCoreTestRunner.dll /unit) > "$out/jit.log" 2>&1
jit_status=$?
(cd "$out" && timeout 900 ./DotNetCoreTestRunner /unit) > "$out/aot.log" 2>&1
aot_status=$?
set -e

echo "JIT:       $(tail -n 1 "$out/jit.log")"
echo "NativeAOT: $(tail -n 1 "$out/aot.log")"

if [ "$jit_status" -ne 0 ]; then
  echo "The JIT run failed (exit $jit_status); see $out/jit.log"
  exit 1
fi

if ! grep -a -q '^Running on AOT : True' "$out/aot.log"; then
  echo "MoonSharp did not detect AOT in the NativeAOT build; see $out/aot.log"
  exit 1
fi

failed_tests "$out/aot.log" > "$out/failed.txt"

if [ "$aot_status" -ne "$(wc -l < "$out/failed.txt")" ]; then
  echo "The NativeAOT runner exited with $aot_status, but $(wc -l < "$out/failed.txt") failures were found in the log; see $out/aot.log"
  exit 1
fi

if [ "$(discovered_tests "$out/aot.log")" != "$(discovered_tests "$out/jit.log")" ]; then
  echo "The NativeAOT build found $(discovered_tests "$out/aot.log") tests, the JIT build $(discovered_tests "$out/jit.log"): trimming removed tests"
  exit 1
fi

# One test name per line, then optionally "# reason".
awk '!/^[[:space:]]*(#|$)/ { print $1 }' "$here/nativeaot-known-failures.txt" | sort -u > "$out/known.txt"

unexpected="$(comm -23 "$out/failed.txt" "$out/known.txt")"
fixed="$(comm -13 "$out/failed.txt" "$out/known.txt")"

if [ -n "$unexpected" ]; then
  echo "New NativeAOT failures (see $out/aot.log):"
  echo "$unexpected"
fi

if [ -n "$fixed" ]; then
  echo "Listed in nativeaot-known-failures.txt but passing now; remove them:"
  echo "$fixed"
fi

[ -z "$unexpected" ] && [ -z "$fixed" ]
