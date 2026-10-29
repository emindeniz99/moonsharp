#!/usr/bin/env bash
# Interleaved per-process runs: each round holds the shared benchmark lock once and runs every engine
# once, as its own process with a timeout, in an order that rotates per round.
#
# Usage: run.sh <outdir> <first round> <last round> <label=command>...
#   label: <aot|mono>_<engine>, the names agg.py reads (engine: next, next2, spike, spikeoff, luacs)
# Example: run.sh runs 1 11 "aot_next=aot-next/b1 3" "aot_spike=aot-spike/b1 3" "aot_spikeoff=aot-spike/b1 3 off" \
#            "mono_next=mono b1-mono-next/b1.exe 3"
# Output: <outdir>/<round>-<label>.tsv and .err; aggregate with agg.py <outdir>.
set -u
if [ $# -lt 4 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
OUT=$1; FIRST=$2; LAST=$3; shift 3
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
mkdir -p "$OUT"
SPECS=("$@"); n=${#SPECS[@]}
for r in $(seq "$FIRST" "$LAST"); do
	(
		flock 9
		for j in $(seq 0 $((n - 1))); do
			spec=${SPECS[$(( (j + r) % n ))]}
			label=${spec%%=*}; cmd=${spec#*=}
			# shellcheck disable=SC2086 # the command is split into words on purpose
			timeout 900 $cmd > "$OUT/$r-$label.tsv" 2> "$OUT/$r-$label.err" || echo "FAIL $label"
		done
		echo "round $r $(cut -d" " -f1-3 /proc/loadavg)"
	) 9>> "$LOCK"
done
echo done
