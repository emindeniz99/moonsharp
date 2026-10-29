#!/usr/bin/env bash
# Runs Lua-CSharp's own BenchmarkDotNet suites (sandbox/Benchmark at a8d84c7 with
# docs/research/lua-csharp-own-bench.patch applied and the fork built into sandbox/Benchmark/fork).
# Each suite holds the shared benchmark lock for its whole run.
#
# Usage: run-suites.sh <Lua-CSharp checkout> <artifacts dir> <label> <suite>...
#   suites: InterpreterComparison, CompilationComparison, StartupComparison
# Output: <artifacts dir>/<label>-<suite>/; combine runs with combine_bdn.py.
set -u
if [ $# -lt 4 ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
LCS=$(cd "$1" && pwd); ART=$(mkdir -p "$2" && cd "$2" && pwd); LABEL=$3; shift 3
B=$LCS/sandbox/Benchmark/bin/Release/net10.0/Benchmark.dll
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# BenchmarkDotNet builds its generated project inside the lock: keep no MSBuild or compiler server
# alive afterwards, or it inherits the lock's file descriptor and holds it.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
# BenchmarkDotNet finds Benchmark.csproj from the working directory (their README: "Run from this directory").
cd "$LCS/sandbox/Benchmark" || exit 1
for suite in "$@"; do
	out=$ART/$LABEL-$suite
	mkdir -p "$out"
	echo "$(date +%T) waiting for lock: $suite"
	flock "$LOCK" bash -c "echo \"\$(date +%T) start $suite load=\$(cut -d' ' -f1-3 /proc/loadavg)\"; timeout 5400 dotnet '$B' --filter '*$suite*' --exporters json --artifacts '$out' > '$out/console.log' 2>&1; echo \"\$(date +%T) end $suite exit=\$? load=\$(cut -d' ' -f1-3 /proc/loadavg)\""
done
echo done
