#!/usr/bin/env bash
# Summarises benchmark results files (rows written by src/DevTools/Benchmarks/WorkloadTsv.cs, in the
# Unity test bed with -moonsharpBench): DIR/PREFIX-SIDE-rROUND.tsv, where SIDE is "this" (the build
# under test), "this2" (the same build run again, which bounds the noise) or "baseline" (the build
# it is compared with, run on the same machine in the same rounds). Prints a Markdown table, also to
# the job summary under GitHub Actions. Ratios are the median of the per-round ratios, with their
# range. Fails when a workload returned a wrong check value or a results file lacks a workload.
# Usage: bench-summary.sh LABEL DIR PREFIX THIS_NAME [BASELINE_NAME]
set -euo pipefail
export LC_ALL=C

if [ $# -lt 4 ]; then
  echo "usage: $0 LABEL DIR PREFIX THIS_NAME [BASELINE_NAME]" >&2
  exit 2
fi
label="$1"
dir="$2"
prefix="$3"
this_name="$4"
baseline_name="${5:-baseline}"

shopt -s nullglob
files=("${dir}/${prefix}"-*-r*.tsv)
if [ ${#files[@]} -eq 0 ]; then
  echo "::error::${label}: no benchmark results in ${dir}"
  exit 1
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

awk -F'\t' -v errors="$tmp/errors" -v this_name="$this_name" -v baseline_name="$baseline_name" '
  function sorted(list, a,    n, i, j, t) {
    n = split(list, a, " ")
    for (i = 2; i <= n; i++) {
      t = a[i]
      for (j = i - 1; j >= 1 && a[j] + 0 > t + 0; j--) a[j + 1] = a[j]
      a[j + 1] = t
    }
    return n
  }
  function median(list,    a, n) {
    n = sorted(list, a)
    return n % 2 ? a[(n + 1) / 2] : (a[n / 2] + a[n / 2 + 1]) / 2
  }
  # "median (min-max)" of the per-round ratios num/den, or "-" when no round has both.
  function ratios(num, den, c,    r, list, a, n) {
    list = ""
    for (r in rounds)
      if ((num, r, c) in ns && (den, r, c) in ns && ns[den, r, c] > 0)
        list = list " " ns[num, r, c] / ns[den, r, c]
    if (list == "") return "-"
    n = sorted(list, a)
    return sprintf("%.3f (%.3f-%.3f)", median(list), a[1], a[n])
  }
  function side_median(side, c,    r, list) {
    list = ""
    for (r in rounds)
      if ((side, r, c) in ns) list = list " " ns[side, r, c]
    return list == "" ? "-" : sprintf("%.1f", median(list))
  }
  FNR == 1 {
    name = FILENAME
    sub(/.*\//, "", name)
    sub(/\.tsv$/, "", name)
    n = split(name, part, "-")
    side = part[n - 1]
    round = substr(part[n], 2)
    rounds[round] = 1
    files[side]++
  }
  {
    if (!($1 in seen)) { seen[$1] = 1; order[++cases] = $1 }
    ns[side, round, $1] = $3
    count[side, $1]++
    if ($7 != "OK") print FILENAME ": " $1 " returned " $6 > errors
  }
  END {
    print "| Workload | " this_name " (ns/iter) | A/A: this again / this | " baseline_name " (ns/iter) | this / baseline |"
    print "|---|---:|---:|---:|---:|"
    for (i = 1; i <= cases; i++) {
      c = order[i]
      print "| " c " | " side_median("this", c) " | " ratios("this2", "this", c) " | " side_median("baseline", c) " | " ratios("this", "baseline", c) " |"
      for (s in files)
        if (count[s, c] != files[s]) print s ": " c " is in " count[s, c] + 0 " of " files[s] " results files" > errors
    }
  }
' "${files[@]}" > "$tmp/table.md"

{
  echo "### Benchmarks, ${label}"
  echo
  echo "${#files[@]} results files in ${dir} (${prefix}-*). ns per iteration: median over the processes of each process's median of its timed runs. Ratios below 1 mean faster than the reference."
  echo
  cat "$tmp/table.md"
} > "$tmp/report.md"

cat "$tmp/report.md"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then cat "$tmp/report.md" >> "$GITHUB_STEP_SUMMARY"; fi

if [ -s "$tmp/errors" ]; then
  while IFS= read -r line; do echo "::error::${label}: ${line}"; done < "$tmp/errors"
  exit 1
fi
