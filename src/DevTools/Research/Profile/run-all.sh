#!/usr/bin/env bash
# Collects a CPU-sample and allocation-tick trace for each workload, one at a time, then analyses it.
#
# Usage: DOTNET_TRACE=<dotnet-trace> PROFILE=<Profile build dir> ANALYZER=<Analyzer.dll> run-all.sh <tracedir> <workload>...
#   SECS: seconds per workload (default 10)
# Output: <tracedir>/<workload>.nettrace, .run.txt and .analysis.txt; summarize.py <tracedir> builds the tables.
set -u
if [ $# -lt 2 ] || [ -z "${DOTNET_TRACE:-}" ] || [ -z "${PROFILE:-}" ] || [ -z "${ANALYZER:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
T=$1; shift
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p "$T"
for w in "$@"; do
	rm -f "$T/$w.nettrace" "$T/$w.etlx"
	timeout 180 "$DOTNET_TRACE" collect --profile dotnet-sampled-thread-time --providers Microsoft-Windows-DotNETRuntime:0x100003801D:5 \
		-o "$T/$w.nettrace" --show-child-io -- dotnet "$PROFILE/Profile.dll" loop "$w" "${SECS:-10}" > "$T/$w.run.txt" 2>&1
	timeout 300 dotnet "$ANALYZER" "$T/$w.nettrace" 30 "$PROFILE" > "$T/$w.analysis.txt" 2>&1
	echo "$w: $(grep DONE "$T/$w.run.txt")"
done
