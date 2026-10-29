#!/bin/bash
# Runs every differential case file on the fork with msrun and prints compare.py's totals per file.
#
# Usage: difall.sh <msrun.dll>
# Writes src/MoonSharp.Interpreter.Tests/Lua52Differential/observed/<case>.txt.
if [ $# -ne 1 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
MSRUN=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
D=$(cd "$(dirname "$0")/../../../MoonSharp.Interpreter.Tests/Lua52Differential" && pwd)
ls "$D/cases" | sed 's/\.lua$//' | xargs -P3 -I{} "$D/run_fork.sh" {} dotnet "$MSRUN"
for f in "$D"/cases/*.lua; do n=$(basename "$f" .lua); printf '%-12s ' "$n"; python3 -I "$D/compare.py" "$D/expected/$n.txt" "$D/observed/$n.txt"; done
