#!/bin/sh
# Runs the hosting fixtures' logic suite with one order variant.
#
# Usage: SPIKE=<worktree of spike/table-order, MoonSharp.Hosting.Cli built> runhost.sh <a|b|c|probe mode|"">
if [ $# -ne 1 ] || [ -z "${SPIKE:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
export MOONSHARP_ORDER_PROBE="$1"
export MOONSHARP_TABLE_ORDER="$1"
exec timeout 300 dotnet "$SPIKE/src/Hosting/MoonSharp.Hosting.Cli/bin/Release/net8.0/moonsharp-host.dll" test "$SPIKE/src/Hosting/fixtures/logic.suite.json"
