#!/usr/bin/env bash
# Sequential A/B: one benchmark process at a time, each under the shared benchmark lock.
#
# Usage: ab.sh <variant=Profile build dir>...   (RUNS: calls per workload, default 5)
# Prints "### variant <name>" and the bench table for each; abtable.py reads the saved output.
set -u
if [ $# -lt 1 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
for spec in "$@"; do
	v=${spec%%=*}; d=${spec#*=}
	echo "### variant $v"
	flock "$LOCK" timeout 900 dotnet "$d/Profile.dll" bench "${RUNS:-5}"
done
