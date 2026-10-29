#!/usr/bin/env bash
# Builds MoonSharp.Interpreter (netstandard2.0, Release) at a commit into $WORK/dll/<short sha>/,
# using a detached worktree at $WORK/wt. Builds outside the benchmark lock, with no build servers.
#
# Usage: WORK=<scratch dir> build-at.sh <commit>
# Create the worktree once: git worktree add --detach "$WORK/wt" origin/next
set -eu
if [ $# -ne 1 ] || [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
WT=$WORK/wt
sha=$(git -C "$WT" rev-parse --short=7 "$1")
out=$WORK/dll/$sha
[ -f "$out/MoonSharp.Interpreter.dll" ] && { echo "$out (cached)"; exit 0; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
git -C "$WT" checkout -q --detach "$sha"
git -C "$WT" clean -qfdx src/MoonSharp.Interpreter
mkdir -p "$WORK/logs"
timeout 600 dotnet build "$WT/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj" -c Release -f netstandard2.0 \
	--disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false -v q -o "$out" > "$WORK/logs/build-$sha.log" 2>&1 \
	|| { echo "build failed at $sha"; tail -5 "$WORK/logs/build-$sha.log"; exit 1; }
echo "$out"
