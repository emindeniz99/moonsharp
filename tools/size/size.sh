#!/usr/bin/env bash
# What MoonSharp adds to an app's size. CI runs it in the size job of .github/workflows/ci.yml;
# docs/ROADMAP.md ("Size tracking") says what is measured where.
#
#   tools/size/size.sh [--nativeaot] [output directory]
#
# Builds the netstandard2.0 Release MoonSharp.Interpreter.dll and fails when its size differs
# from interpreter-dll.baseline by more than one 512-byte step.
#
# With --nativeaot it also publishes probe/ (one script through a default Script) and hello/
# (the same program without MoonSharp) with NativeAOT for linux-x64, runs the probe, and reports
# both binaries and their difference. Those numbers have no baseline yet. Needs clang and
# zlib1g-dev.
#
# The report goes to stdout and, under GitHub Actions, to the job summary.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
nativeaot=false
if [ "${1:-}" = --nativeaot ]; then
  nativeaot=true
  shift
fi
out="$(mkdir -p "${1:-$here/bin}" && cd "${1:-$here/bin}" && pwd)"

# The sections of a PE file are padded to 512 bytes, so the DLL's size moves in 512-byte steps
# and a change of a few bytes can cross one. The build is deterministic: the same source gives
# the same bytes.
tolerance=512
baseline="$(grep -v '^#' "$here/interpreter-dll.baseline" | tr -d '[:space:]')"

# Without these the version stamp (MinVer, from git history) and the PDB path (the checkout
# directory) inside the binaries differ between a CI checkout and a local one.
props=(-p:MinVerSkip=true -p:ContinuousIntegrationBuild=true)

# 1234567 -> 1,234,567
commas() { sed -E ':a;s/([0-9])([0-9]{3})($|,)/\1,\2\3/;ta' <<< "$1"; }
size() { stat -c %s "$1"; }
gzipped() { gzip -9 -c "$1" | wc -c; }

report="$out/report.md"
status=0
fail() {
  echo "$1" >&2
  if [ "${GITHUB_ACTIONS:-}" = true ]; then echo "::error::$1"; fi
  status=1
}

dotnet build "$root/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj" -c Release -f netstandard2.0 \
  "${props[@]}" -o "$out/interpreter" -nologo > "$out/interpreter.log" 2>&1 \
  || { tail -n 50 "$out/interpreter.log"; exit 1; }
dll="$(size "$out/interpreter/MoonSharp.Interpreter.dll")"

{
  echo "### Size"
  echo
  echo "| Binary | Bytes | gzip -9 |"
  echo "|---|---:|---:|"
  echo "| MoonSharp.Interpreter.dll, netstandard2.0 Release (baseline $(commas "$baseline") ± $tolerance) | $(commas "$dll") | $(commas "$(gzipped "$out/interpreter/MoonSharp.Interpreter.dll")") |"
} > "$report"

if [ "$dll" -gt $((baseline + tolerance)) ]; then
  fail "MoonSharp.Interpreter.dll grew to $dll bytes, $((dll - baseline)) over the baseline of $baseline. If that is intended, put $dll in tools/size/interpreter-dll.baseline in the same commit."
elif [ "$dll" -lt $((baseline - tolerance)) ]; then
  fail "MoonSharp.Interpreter.dll shrank to $dll bytes, $((baseline - dll)) under the baseline of $baseline. Put $dll in tools/size/interpreter-dll.baseline, so later growth is measured from there."
fi

if [ "$nativeaot" = true ]; then
  for project in hello probe; do
    dotnet publish "$here/$project" -c Release -r linux-x64 "${props[@]}" -o "$out/$project" -nologo \
      > "$out/$project.log" 2>&1 || { tail -n 50 "$out/$project.log"; exit 1; }
  done
  hello="$out/hello/Hello"
  probe="$out/probe/SizeProbe"

  # A probe that fails would measure a trimmed-away library, not the one an app gets.
  result="$(timeout 60 "$probe" 2>&1 || true)"
  if [ "$result" != abab42 ]; then
    fail "The NativeAOT probe printed '$result' instead of 'abab42'."
  fi

  {
    echo "| NativeAOT linux-x64, one script through MoonSharp (tools/size/probe) | $(commas "$(size "$probe")") | $(commas "$(gzipped "$probe")") |"
    echo "| NativeAOT linux-x64, the same without MoonSharp (tools/size/hello) | $(commas "$(size "$hello")") | $(commas "$(gzipped "$hello")") |"
    echo "| NativeAOT, MoonSharp's share (difference) | $(commas $(($(size "$probe") - $(size "$hello")))) | $(commas $(($(gzipped "$probe") - $(gzipped "$hello")))) |"
    echo
    echo "NativeAOT binaries are stripped (symbols in the .dbg files next to them); gzip -9 approximates their share of a compressed package."
  } >> "$report"
fi

cat "$report"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then cat "$report" >> "$GITHUB_STEP_SUMMARY"; fi
exit "$status"
