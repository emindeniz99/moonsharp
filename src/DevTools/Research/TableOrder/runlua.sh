#!/bin/sh
# Runs a Lua file in the REPL of a worktree with one order or probe mode.
#
# Usage: SPIKE=<worktree, src/MoonSharp built> runlua.sh <a|b|c|probe mode|""> <file.lua>
if [ $# -ne 2 ] || [ -z "${SPIKE:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
export MOONSHARP_ORDER_PROBE="$1"
export MOONSHARP_TABLE_ORDER="$1"
exec timeout 120 dotnet "$SPIKE/src/MoonSharp/bin/Release/net8.0/MoonSharp.dll" "$2"
