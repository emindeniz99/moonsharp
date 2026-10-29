#!/bin/sh
# Builds the spike interpreter once per variant into $WORK/bin-a, bin-b, bin-c, bin-d, and copies the
# spike's Benchmarks build (with its table workloads) into $WORK/bench-spike.
#
# Usage: SPIKE=<worktree of spike/table-order> WORK=<scratch dir> build_variants.sh
#   variants: a (default build), b (-p:TableOrder=B), c (-p:TableOrder=C), d (-p:TableOrderDynamic=true)
set -e
if [ -z "${SPIKE:-}" ] || [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
P=$SPIKE/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj
OUT=$SPIKE/src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll
build() {
	dotnet build -c Release -f netstandard2.0 "$P" --no-incremental --disable-build-servers "$@" 2>&1 | grep -E "Warn|Error\(s\)" | grep -v "warning CS" || true
}
build; mkdir -p "$WORK/bin-a"; cp "$OUT" "$WORK/bin-a/"
build -p:TableOrder=B; mkdir -p "$WORK/bin-b"; cp "$OUT" "$WORK/bin-b/"
build -p:TableOrder=C; mkdir -p "$WORK/bin-c"; cp "$OUT" "$WORK/bin-c/"
build -p:TableOrderDynamic=true; mkdir -p "$WORK/bin-d"; cp "$OUT" "$WORK/bin-d/"
build
sha256sum "$WORK"/bin-*/MoonSharp.Interpreter.dll
dotnet build -c Release --disable-build-servers "$SPIKE/src/DevTools/Benchmarks/Benchmarks.csproj" 2>&1 | grep -E " error |Error\(s\)"
rm -rf "$WORK/bench-spike"; cp -r "$SPIKE/src/DevTools/Benchmarks/bin/Release/net8.0" "$WORK/bench-spike"
