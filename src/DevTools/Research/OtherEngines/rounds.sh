#!/usr/bin/env bash
# Runs the OtherEngines builds in rounds. Each round holds the shared benchmark lock (at most 1 h) and
# runs, for each mode (workloads, interop, create), every engine in its own process with a timeout.
#
# Usage: rounds.sh <outdir> <first round> <last round> <label=path/to/build.dll>...
# Example: rounds.sh out 1 7 msA=ms/ms.dll ws=ws/ws.dll msB=ms/ms.dll msup=msup/msup.dll
# Output: <outdir>/r<round>-<mode>-<label>.jsonl and .err; aggregate with aggregate.py <outdir>.
set -u
if [ $# -lt 4 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
OUT=$1; FIRST=$2; LAST=$3; shift 3
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"
round() {
	local r=$1; shift
	for mode in workloads interop create; do
		for spec in "$@"; do
			local label=${spec%%=*} exe=${spec#*=}
			timeout 900 dotnet "$exe" "$mode" 7 > "$OUT/r$r-$mode-$label.jsonl" 2> "$OUT/r$r-$mode-$label.err"
			echo "$(date +%T) r$r $mode $label exit=$? load=$(cut -d' ' -f1-3 /proc/loadavg)"
		done
	done
}
export -f round; export OUT
for r in $(seq "$FIRST" "$LAST"); do
	flock "$LOCK" timeout 3600 bash -c 'round "$@"' _ "$r" "$@"
	echo "$(date +%T) round $r done rc=$?"
done
echo done
