#!/usr/bin/env bash
# git bisect run step: builds the current commit of $WORK/wt and classifies it by the number of gen2
# GCs per 64-load batch of the control-flow source on a clean heap (deterministic, not timing).
# Exit 0 (good): < 0.5 gen2 per batch; 1 (bad): >= 0.5; 125 (skip): the build failed.
#
# Usage: WORK=<scratch dir> CFLOW=<CFlow.dll> git -C "$WORK/wt" bisect run <this script>
set -u
if [ -z "${WORK:-}" ] || [ -z "${CFLOW:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WT=$WORK/wt
sha=$(git -C "$WT" rev-parse --short=7 HEAD)
out=$WORK/dll/$sha
if [ ! -f "$out/MoonSharp.Interpreter.dll" ]; then
	mkdir -p "$WORK/logs"
	git -C "$WT" clean -qfdx src/MoonSharp.Interpreter
	timeout 600 dotnet build "$WT/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj" -c Release -f netstandard2.0 \
		--disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false -v q -o "$out" > "$WORK/logs/build-$sha.log" 2>&1 || exit 125
fi
line=$(CFLOW_CLEAN=1 timeout 600 dotnet "$CFLOW" "$out/MoonSharp.Interpreter.dll" full 40 8 batch)
echo "$sha $line" >> "$WORK/bisect.log"
g2=$(sed -E 's/.*gen2\/batch=([0-9.]+).*/\1/' <<< "$line")
awk -v g="$g2" 'BEGIN { exit (g >= 0.5) ? 1 : 0 }'
