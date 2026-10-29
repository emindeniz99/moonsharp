#!/bin/bash
# Runs one case file on MoonSharp and writes observed/<name>.txt. A case that
# ends the process (an exception that pcall cannot catch) or runs past 60 s is
# recorded as CRASH or HANG, and the run goes on with the next case.
#   ./run_fork.sh <name> <command that runs a Lua file with MoonSharp...>
# The command gets run.lua, the case file and a start index as arguments.
set -f
here=$(cd "$(dirname "$0")" && pwd)
name=$1; shift
cases=$here/cases/$name.lua
out=$here/observed/$name.txt
err=$(mktemp)
: > "$out"
total=$(grep -c '^--@@ ' "$cases")
start=1
while [ "$start" -le "$total" ]; do
  timeout -k 5 60 "$@" "$here/run.lua" "$cases" "$start" >> "$out" 2> "$err"
  rc=$?
  done_n=$(wc -l < "$out")
  if [ "$rc" -eq 0 ] || [ "$done_n" -ge "$total" ]; then break; fi
  case_name=$(grep '^--@@ ' "$cases" | sed -n "$((done_n + 1))p" | sed 's/^--@@ //')
  if [ "$rc" -eq 124 ] || [ "$rc" -eq 137 ]; then
    printf '%s\tHANG\n' "$case_name" >> "$out"
  else
    printf '%s\tCRASH\t%s\n' "$case_name" "$(grep -a -m1 -o '[A-Za-z.]*Exception' "$err")" >> "$out"
  fi
  start=$((done_n + 2))
done
rm -f "$err"
