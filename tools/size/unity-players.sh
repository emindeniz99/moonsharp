#!/usr/bin/env bash
# Reports what MoonSharp adds to a Linux IL2CPP player: the same project built without and with
# MoonSharp (src/Unity/SizeProbe, built twice by .github/workflows/unity.yml).
#
#   tools/size/unity-players.sh LABEL PLAYER_WITHOUT_DIR PLAYER_WITH_DIR [NOTE]
#
# Measures the files a player ships (not the *_DontShipItWithYourGame folder IL2CPP writes next
# to it), GameAssembly.so (the IL2CPP native code; also a stripped copy and gzip -9 of that) and
# global-metadata.dat. The report goes to stdout and, under GitHub Actions, to the job summary.
set -euo pipefail

if [ $# -lt 3 ]; then
  echo "usage: $0 LABEL PLAYER_WITHOUT_DIR PLAYER_WITH_DIR [NOTE]" >&2
  exit 2
fi
label="$1"
without="$2"
with="$3"
note="${4:-}"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# 1234567 -> 1,234,567; keeps a leading minus sign.
commas() { sed -E ':a;s/([0-9])([0-9]{3})($|,)/\1,\2\3/;ta' <<< "$1"; }
shipped() { find "$1" -type f -not -path '*DontShip*' -printf '%s\n' | awk '{ s += $1 } END { print s + 0 }'; }
one() {
  local found
  found="$(find "$1" -type f -name "$2" -not -path '*DontShip*')"
  if [ "$(wc -l <<< "$found")" -ne 1 ] || [ -z "$found" ]; then
    echo "Expected one $2 in $1, found: ${found:-none}" >&2
    exit 1
  fi
  echo "$found"
}
size() { stat -c %s "$1"; }
stripped() { cp "$1" "$tmp/s" && strip --strip-unneeded "$tmp/s" && size "$tmp/s"; }
stripped_gzip() { cp "$1" "$tmp/s" && strip --strip-unneeded "$tmp/s" && gzip -9 -c "$tmp/s" | wc -c; }

row() { # name, value without, value with
  echo "| $1 | $(commas "$2") | $(commas "$3") | $(commas $(($3 - $2))) |"
}

ga_without="$(one "$without" GameAssembly.so)"
ga_with="$(one "$with" GameAssembly.so)"
md_without="$(one "$without" global-metadata.dat)"
md_with="$(one "$with" global-metadata.dat)"

{
  echo "### Size, $label, Linux IL2CPP player"
  echo
  if [ -n "$note" ]; then
    echo "$note"
    echo
  fi
  echo "| Bytes | Without MoonSharp | With MoonSharp | MoonSharp adds |"
  echo "|---|---:|---:|---:|"
  row "Player, all shipped files" "$(shipped "$without")" "$(shipped "$with")"
  row "GameAssembly.so" "$(size "$ga_without")" "$(size "$ga_with")"
  row "GameAssembly.so, stripped" "$(stripped "$ga_without")" "$(stripped "$ga_with")"
  row "GameAssembly.so, stripped, gzip -9" "$(stripped_gzip "$ga_without")" "$(stripped_gzip "$ga_with")"
  row "global-metadata.dat" "$(size "$md_without")" "$(size "$md_with")"
} > "$tmp/report.md"

cat "$tmp/report.md"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then cat "$tmp/report.md" >> "$GITHUB_STEP_SUMMARY"; fi
