#!/bin/sh
# Runs the unit suite of the spike with one order variant.
#
# Usage: SPIKE=<worktree of spike/table-order, DotNetCoreTestRunner built> runtests.sh <a|b|c|probe mode|""> <outfile>
#   The mode sets MOONSHARP_TABLE_ORDER (spike) and MOONSHARP_ORDER_PROBE (scramble-probe.patch).
if [ $# -ne 2 ] || [ -z "${SPIKE:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
export MOONSHARP_ORDER_PROBE="$1"
export MOONSHARP_TABLE_ORDER="$1"
out=$(cd "$(dirname "$2")" && pwd)/$(basename "$2")
cd "$SPIKE/src/TestRunners/DotNetCoreTestRunner/bin/Release/net8.0" || exit 9
timeout 900 dotnet DotNetCoreTestRunner.dll /unit > "$out" 2>&1
echo "rc=$? $(tail -1 "$out")"
