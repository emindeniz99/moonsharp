#!/bin/sh
# Like runlua-all.sh, with master's CollectDeadKeys fixed (the REPL in $WORK/repl-masterfix, built from a
# master worktree patched by masterfix.py) as "master".
#
# Usage: SPIKE=<worktree of spike/table-order, src/MoonSharp built> WORK=<scratch dir> runlua-mf.sh <file.lua>
if [ $# -ne 1 ] || [ -z "${SPIKE:-}" ] || [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
REPL=$SPIKE/src/MoonSharp/bin/Release/net8.0/MoonSharp.dll
echo "== master"
timeout 300 dotnet "$WORK/repl-masterfix/MoonSharp.dll" "$1"
for m in a b c; do
	echo "== $m"
	MOONSHARP_TABLE_ORDER=$m timeout 300 dotnet "$REPL" "$1"
done
