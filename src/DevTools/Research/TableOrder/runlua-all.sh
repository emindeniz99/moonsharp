#!/bin/sh
# Runs a Lua file under master (the REPL built at master, if $WORK/repl-master exists) and the spike's
# variants a, b and c. cmp.py compares the sections.
#
# Usage: SPIKE=<worktree of spike/table-order, src/MoonSharp built> WORK=<scratch dir> runlua-all.sh <file.lua>
if [ $# -ne 1 ] || [ -z "${SPIKE:-}" ] || [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
REPL=$SPIKE/src/MoonSharp/bin/Release/net8.0/MoonSharp.dll
if [ -f "$WORK/repl-master/MoonSharp.dll" ]; then
	echo "== master"
	timeout 300 dotnet "$WORK/repl-master/MoonSharp.dll" "$1"
fi
for m in a b c; do
	echo "== $m"
	MOONSHARP_TABLE_ORDER=$m timeout 300 dotnet "$REPL" "$1"
done
