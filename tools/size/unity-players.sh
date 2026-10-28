#!/usr/bin/env bash
# Reports what MoonSharp adds to a Linux IL2CPP player: the same project built without and with
# MoonSharp (src/Unity/SizeProbe, built twice by .github/workflows/unity.yml).
#
#   tools/size/unity-players.sh LABEL PLAYER_WITHOUT_DIR PLAYER_WITH_DIR [NOTE] [PLAYER_BASELINE_DIR]
#
# Measures the files a player ships (not the *_DontShipItWithYourGame folder IL2CPP writes next
# to it), GameAssembly.so (the IL2CPP native code; also a stripped copy and gzip -9 of that) and
# global-metadata.dat. The report goes to stdout and, under GitHub Actions, to the job summary.
# PLAYER_BASELINE_DIR is the same project built with MoonSharp from another ref (the benchmark
# baseline); it adds what that MoonSharp adds and the difference between the two.
set -euo pipefail

if [ $# -lt 3 ]; then
  echo "usage: $0 LABEL PLAYER_WITHOUT_DIR PLAYER_WITH_DIR [NOTE] [PLAYER_BASELINE_DIR]" >&2
  exit 2
fi
label="$1"
without="$2"
with="$3"
note="${4:-}"
baseline="${5:-}"

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

measure() { # what, player dir
  case "$1" in
    shipped) shipped "$2" ;;
    ga) size "$(one "$2" GameAssembly.so)" ;;
    ga-stripped) stripped "$(one "$2" GameAssembly.so)" ;;
    ga-gzip) stripped_gzip "$(one "$2" GameAssembly.so)" ;;
    metadata) size "$(one "$2" global-metadata.dat)" ;;
  esac
}
row() { # name, what
  local a b c
  a="$(measure "$2" "$without")"
  b="$(measure "$2" "$with")"
  if [ -n "$baseline" ]; then
    c="$(measure "$2" "$baseline")"
    echo "| $1 | $(commas "$a") | $(commas "$b") | $(commas $((b - a))) | $(commas $((c - a))) | $(commas $((b - c))) |"
  else
    echo "| $1 | $(commas "$a") | $(commas "$b") | $(commas $((b - a))) |"
  fi
}

# Fail before writing anything when a player is incomplete.
for dir in "$without" "$with" ${baseline:+"$baseline"}; do
  one "$dir" GameAssembly.so > /dev/null
  one "$dir" global-metadata.dat > /dev/null
done

{
  echo "### Size, $label, Linux IL2CPP player"
  echo
  if [ -n "$note" ]; then
    echo "$note"
    echo
  fi
  if [ -n "$baseline" ]; then
    echo "| Bytes | Without MoonSharp | With MoonSharp | MoonSharp adds | Baseline MoonSharp adds | Difference to baseline |"
    echo "|---|---:|---:|---:|---:|---:|"
  else
    echo "| Bytes | Without MoonSharp | With MoonSharp | MoonSharp adds |"
    echo "|---|---:|---:|---:|"
  fi
  row "Player, all shipped files" shipped
  row "GameAssembly.so" ga
  row "GameAssembly.so, stripped" ga-stripped
  row "GameAssembly.so, stripped, gzip -9" ga-gzip
  row "global-metadata.dat" metadata
} > "$tmp/report.md"

cat "$tmp/report.md"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then cat "$tmp/report.md" >> "$GITHUB_STEP_SUMMARY"; fi
