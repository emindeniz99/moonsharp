#!/usr/bin/env bash
# Runs the interleaved bench harness in two passes, each a separate process holding the shared
# benchmark lock, with a timeout.
#
# Usage: run-bench.sh <bench.dll> <outdir> <rounds> <label=path/MoonSharp.Interpreter.dll[:off]>... [luacs]
# Example: run-bench.sh bench.dll out 21 next=next/MoonSharp.Interpreter.dll spike=spike/MoonSharp.Interpreter.dll spikeoff=spike/MoonSharp.Interpreter.dll:off luacs
# Output: <outdir>/bench-pass1.txt and bench-pass2.txt.
set -u
if [ $# -lt 4 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
B=$1; OUT=$2; shift 2
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$OUT"
for p in 1 2; do
	flock "$LOCK" timeout 1200 dotnet "$B" "$@" > "$OUT/bench-pass$p.txt" 2>&1
	echo "pass $p exit $?"
done
