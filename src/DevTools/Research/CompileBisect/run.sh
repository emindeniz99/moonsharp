#!/usr/bin/env bash
# Times Script.LoadString on several MoonSharp builds. Holds the shared benchmark lock for the whole
# call; each process has a timeout.
#
# Usage: CFLOW=<CFlow.dll> [WORK=<scratch dir>] run.sh "<builds>" "<variant:repeats:batch|fresh ...>" [batches]
#   builds: nuget (the MoonSharp 2.0.0 package in the NuGet cache), a short sha built by build-at.sh
#   into $WORK/dll, or a path to a MoonSharp.Interpreter.dll
# Example: run.sh "nuget fbd0367 a700bca" "full:40:batch plain:640:batch" 15
set -u
if [ $# -lt 2 ] || [ -z "${CFLOW:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
LABELS=$1; CASES=$2; BATCHES=${3:-15}
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
NUGET=${NUGET_PACKAGES:-$HOME/.nuget/packages}/moonsharp/2.0.0/lib/netstandard1.6/MoonSharp.Interpreter.dll
export DOTNET_CLI_TELEMETRY_OPTOUT=1 CFLOW LABELS CASES BATCHES NUGET WORK="${WORK:-}"
flock "$LOCK" bash -c '
echo "load $(cut -d" " -f1-3 /proc/loadavg)"
for c in $CASES; do IFS=: read v n m <<< "$c"
  for l in $LABELS; do
    case $l in nuget) d=$NUGET;; /*|./*) d=$l;; *) d=$WORK/dll/$l/MoonSharp.Interpreter.dll;; esac
    printf "%-8s " "$l"; timeout 600 dotnet "$CFLOW" "$d" "$v" "$n" "$BATCHES" "$m"
  done
done
echo "load $(cut -d" " -f1-3 /proc/loadavg)"'
