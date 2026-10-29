#!/usr/bin/env bash
# Runs the LuaCsBench harness in rounds. Each round holds the shared benchmark lock once and runs
# every engine in its own process, in the order ms(A), luacs, ms(B), so the two MoonSharp series form
# an A/A pair. Every process has a timeout.
#
# Usage: run-rounds.sh <LuaCsBench.dll> <outdir> <first round> <last round> <all|workloads> [VAR=value ...]
# The VAR=value pairs are exported first (the tc0 series used DOTNET_TieredCompilation=0).
# Output: <outdir>/r<round>-<mode>-<label>.jsonl and .err; aggregate with aggregate.py <outdir>.
set -u
if [ $# -lt 5 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
B=$1; OUT=$2; FIRST=$3; LAST=$4; MODES=$5; shift 5
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
for kv in "$@"; do export "$kv"; done
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"
export OUT B MODES
round() {
	local r=$1
	run() { local label=$1; shift
		timeout 900 dotnet "$B" "$@" > "$OUT/$label.jsonl" 2> "$OUT/$label.err"; local rc=$?
		echo "$(date +%T) $label exit=$rc load=$(cut -d" " -f1-3 /proc/loadavg)"; }
	run "r$r-workloads-msA" workloads ms 7
	run "r$r-workloads-luacs" workloads luacs 7
	run "r$r-workloads-msB" workloads ms 7
	[ "$MODES" = all ] || return 0
	run "r$r-boundary-msA" boundary ms 7
	run "r$r-boundary-luacs" boundary luacs 7
	run "r$r-boundary-msB" boundary ms 7
	run "r$r-create-msA" create ms 7 c_create_default
	run "r$r-create-mscomplete" create ms 7 c_create_complete
	run "r$r-create-luacs" create luacs 7 c_create_stdlib
	run "r$r-create-msB" create ms 7 c_create_default
}
export -f round
for r in $(seq "$FIRST" "$LAST"); do
	flock "$LOCK" bash -c "round $r"
	echo "$(date +%T) round $r finished"
done
echo done
