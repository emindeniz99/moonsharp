#!/usr/bin/env bash
# Interleaved rounds of the workloads-tsv runner, like unity.yml's IL2CPP step, on .NET and on Unity's
# Mono: each round holds the shared benchmark lock and runs this, this2 (the A/A copy) and baseline once
# per runtime, in an order that rotates per round. Summarize with src/Unity/UnityTestBed/bench-summary.sh.
#
# Usage: rounds.sh <outdir> <rounds> <this Benchmarks.dll> <baseline Benchmarks.dll> <this bench.exe> <baseline bench.exe>
#   MONO: Unity's mono binary (default: mono on PATH)
# Output: <outdir>/<dotnet|mono>-<this|this2|baseline>-r<round>.tsv and .log
set -u
if [ $# -ne 6 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
OUT=$1; ROUNDS=$2; NET_THIS=$3; NET_BASE=$4; MONO_THIS=$5; MONO_BASE=$6
M=${MONO:-mono}
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
mkdir -p "$OUT"
cmd() {
	case "$1:$2" in
		dotnet:this|dotnet:this2) echo "dotnet $NET_THIS workloads-tsv";;
		dotnet:baseline) echo "dotnet $NET_BASE workloads-tsv";;
		mono:this|mono:this2) echo "$M $MONO_THIS";;
		mono:baseline) echo "$M $MONO_BASE";;
	esac
}
players=(this this2 baseline)
for round in $(seq 1 "$ROUNDS"); do
	exec 9>>"$LOCK"; flock 9
	for rt in dotnet mono; do
		for k in "${!players[@]}"; do
			side="${players[$(( (k + round) % 3 ))]}"
			# shellcheck disable=SC2046 # the command is split into words on purpose
			timeout 600 $(cmd $rt $side) "$OUT/$rt-$side-r$round.tsv" 5 > "$OUT/$rt-$side-r$round.log" 2>&1 || echo "FAIL $rt $side $round"
		done
	done
	flock -u 9; exec 9>&-
	echo "round $round $(cut -d' ' -f1-3 /proc/loadavg)"
done
