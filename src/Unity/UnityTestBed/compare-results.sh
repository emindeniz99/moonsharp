#!/usr/bin/env bash
# Compares a Unity results file with the .NET reference (both written by SuiteReport):
# fails when a test failed in Unity or is missing from it (not found, or stripped by IL2CPP).
# Tests that skip themselves on Unity are listed but allowed.
# Usage: compare-results.sh REFERENCE.tsv UNITY.tsv LABEL
set -euo pipefail
export LC_ALL=C

reference="$1"
unity="$2"
label="$3"
status=0

if [ ! -s "${unity}" ]; then
  echo "::error::${label}: no results file at ${unity}"
  exit 1
fi

failed="$(awk -F'\t' '$1 == "FAIL"' "${unity}")"
missing="$(comm -23 <(cut -f2 "${reference}" | sort) <(cut -f2 "${unity}" | sort))"
skipped="$(join -t$'\t' -1 1 -2 1 \
  <(awk -F'\t' '$1 == "OK" { print $2 }' "${reference}" | sort) \
  <(awk -F'\t' '$1 == "SKIP" { print $2 "\t" $3 }' "${unity}" | sort))"

echo "${label}: $(cut -f1 "${unity}" | sort | uniq -c | tr '\n' ' ')(reference: $(cut -f1 "${reference}" | sort | uniq -c | tr '\n' ' '))"

if [ -n "${failed}" ]; then
  echo "::error::${label}: $(printf '%s\n' "${failed}" | wc -l) test(s) failed"
  printf '%s\n' "${failed}"
  status=1
fi
if [ -n "${missing}" ]; then
  echo "::error::${label}: $(printf '%s\n' "${missing}" | wc -l) test(s) of the reference did not run"
  printf '%s\n' "${missing}"
  status=1
fi
if [ -n "${skipped}" ]; then
  echo "${label}: skipped on Unity, passed on .NET:"
  printf '%s\n' "${skipped}"
fi

exit "${status}"
