#!/bin/sh
# All timing and memory runs of the table-order spike, one after another, each under the benchmark lock.
#
# Usage: WORK=<scratch dir> chain.sh
#   $WORK needs bin-master (master ed96417's DLL), bin-a..bin-d and bench-spike (build_variants.sh),
#   bin-2c9e546 (the spike's first commit) and bench-master (Benchmarks built at master).
if [ -z "${WORK:-}" ]; then
	awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
	exit 2
fi
H=$(dirname "$0")
LOCK=${BENCH_LOCK:-/tmp/moonsharp-bench.lock}
sh "$H/ab.sh" bin-master bin-master 11 tables - aa_master.md
sh "$H/ab.sh" bin-a bin-a 11 tables a aa_a.md
sh "$H/ab.sh" bin-master bin-a 21 tables a master_vs_a.md
sh "$H/ab.sh" bin-master bin-b 21 tables b master_vs_b.md
sh "$H/ab.sh" bin-master bin-c 21 tables c master_vs_c.md
sh "$H/ab.sh" bin-c bin-a 21 tables - c_vs_a.md
sh "$H/ab.sh" bin-c bin-b 21 tables - c_vs_b.md
sh "$H/ab.sh" bin-a bin-d 21 tables a a_vs_d.md
sh "$H/ab.sh" bin-c bin-d 21 tables c c_vs_d.md
sh "$H/ab.sh" bin-master bin-2c9e546 21 tables - master_vs_2c9e546.md
sh "$H/ab.sh" bin-master bin-a 0 tablemem - mem_master_a.md
sh "$H/ab.sh" bin-b bin-c 0 tablemem - mem_b_c.md
for v in master a b c; do
	if [ "$v" = master ]; then B=$WORK/bench-master; unset MOONSHARP_TABLE_ORDER; else B=$WORK/bench-spike; export MOONSHARP_TABLE_ORDER=$v; fi
	{
		echo "# $(date -u +%FT%TZ) workloads 7 variant=$v load=$(cut -d' ' -f1-3 /proc/loadavg)"
		flock "$LOCK" timeout 1800 dotnet "$B/Benchmarks.dll" workloads 7
		echo "# exit $? at $(date -u +%FT%TZ) load=$(cut -d' ' -f1-3 /proc/loadavg)"
	} > "$WORK/results/workloads_$v.md" 2>&1
done
echo done
