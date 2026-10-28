#!/usr/bin/env sh
# Publishes TrimTest for browser-wasm (trimmed, MoonSharp NOT rooted), then runs it in
# headless Chromium. Needs the .NET 10 SDK, node and Playwright with Chromium.
#
#   src/DevTools/TrimTest/run-smoke.sh                     # the real check
#   src/DevTools/TrimTest/run-smoke.sh -p:RootMoonSharp=true   # extra msbuild args
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="${TRIMTEST_OUT:-$HERE/bin/publish}"

rm -rf "$OUT"
dotnet publish "$HERE/TrimTest.csproj" -c Release -o "$OUT" "$@"

# Size of what ships for the interpreter, raw and brotli.
for f in "$OUT"/wwwroot/_framework/MoonSharp.Interpreter*.wasm; do
    [ -e "$f" ] || continue
    raw=$(wc -c < "$f")
    br=$(wc -c < "$f.br" 2>/dev/null || echo "-")
    echo "size: $(basename "$f") raw=$raw br=$br"
done

node "$HERE/smoke.mjs" "$OUT/wwwroot"
