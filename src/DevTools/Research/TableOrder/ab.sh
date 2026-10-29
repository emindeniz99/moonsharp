#!/bin/sh
# Runs the frozen spike Benchmarks copy's ab mode under the shared benchmark lock, with a timeout.
#
# Usage: WORK=<scratch dir> ab.sh <A dir> <B dir> <rounds> <tables|tablemem> <order: a|b|c|-> <outfile>
#   A and B are folders under $WORK holding MoonSharp.Interpreter.dll (bin-master, bin-a, ...);
#   order sets MOONSHARP_TABLE_ORDER for variant d ("-" leaves it unset). Output: $WORK/results/<outfile>.
if [ $# -ne 6 ] || [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
BENCH=$WORK/bench-spike/Benchmarks.dll
if [ "$5" = "-" ]; then unset MOONSHARP_TABLE_ORDER; else export MOONSHARP_TABLE_ORDER="$5"; fi
mkdir -p "$WORK/results"
{
	echo "# $(date -u +%FT%TZ) A=$1 B=$2 rounds=$3 set=$4 order=$5 nproc=$(nproc) load=$(cut -d' ' -f1-3 /proc/loadavg)"
	cd "$WORK" && flock "$LOCK" timeout 3000 dotnet "$BENCH" ab "$1/MoonSharp.Interpreter.dll" "$2/MoonSharp.Interpreter.dll" "$3" "$4"
	echo "# exit $? at $(date -u +%FT%TZ) load=$(cut -d' ' -f1-3 /proc/loadavg)"
} > "$WORK/results/$6" 2>&1
