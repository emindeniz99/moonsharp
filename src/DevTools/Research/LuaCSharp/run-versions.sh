#!/usr/bin/env bash
# Two builds of the same LuaCsBench Program.cs, one against NuGet LuaCSharp 0.5.8 and one against
# another Lua-CSharp build (main@a8d84c7 in lua-csharp-own-bench.md). Each round holds the shared
# benchmark lock once and runs, each in its own process: 0.5.8 (A), main, 0.5.8 (B). A/B is the A/A
# noise check.
#
# Usage: run-versions.sh <0.5.8 LuaCsBench.dll> <main LuaCsBench.dll> <outdir> <first> <last> [mode=workloads]
# Output: <outdir>/r<round>-<mode>-<v058A|main|v058B>.jsonl; aggregate with aggregate_versions.py <outdir>.
set -u
if [ $# -lt 5 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
V058=$1; MAIN=$2; OUT=$3; FIRST=$4; LAST=$5; MODE=${6:-workloads}
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"
export OUT MODE V058 MAIN
round() {
	local r=$1
	run() { local label=$1 dll=$2
		timeout 900 dotnet "$dll" "$MODE" luacs 7 > "$OUT/r$r-$MODE-$label.jsonl" 2> "$OUT/r$r-$MODE-$label.err"; local rc=$?
		echo "$(date +%T) r$r $label exit=$rc load=$(cut -d" " -f1-3 /proc/loadavg)"; }
	run v058A "$V058"
	run main "$MAIN"
	run v058B "$V058"
}
export -f round
for r in $(seq "$FIRST" "$LAST"); do
	flock "$LOCK" bash -c "round $r"
	echo "$(date +%T) round $r finished"
done
echo done
