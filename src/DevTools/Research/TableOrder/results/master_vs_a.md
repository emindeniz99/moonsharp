# 2026-10-28T11:56:02Z A=bin-master B=bin-a rounds=21 set=tables order=a nproc=4 load=1.11 1.05 1.32
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
A: bin-master/MoonSharp.Interpreter.dll
B: bin-a/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): a
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 573.3 ns | 217.1 ns | 0.410 | 0.371 to 0.497 | 380 B | 212 B |
| table_array_read | 376.9 ns | 373.6 ns | 1.006 | 0.942 to 1.049 | 528 B | 528 B |
| table_hash_set_get | 764.7 ns | 716.3 ns | 0.967 | 0.830 to 0.996 | 912 B | 864 B |
| gc_small_tables | 1.08 µs | 812.9 ns | 0.727 | 0.691 to 0.739 | 1.7 KB | 1.1 KB |
| table_sort_52 | 174.70 µs | 136.21 µs | 0.771 | 0.753 to 0.810 | 125.2 KB | 69.4 KB |
| table_concat | 1.06 µs | 861.7 ns | 0.811 | 0.796 to 0.819 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.11 µs | 923.7 ns | 0.840 | 0.805 to 0.954 | 1.2 KB | 1.1 KB |
| queue_int_keys | 637.3 ns | 557.4 ns | 0.914 | 0.868 to 1.030 | 824 B | 709 B |
| pairs_mixed_128 | 416.9 ns | 420.0 ns | 1.047 | 0.970 to 1.096 | 601 B | 625 B |
| pairs_hash_128 | 408.9 ns | 407.1 ns | 1.003 | 0.971 to 1.030 | 601 B | 601 B |
| record_build_read | 1.30 µs | 889.2 ns | 0.707 | 0.658 to 0.730 | 1.8 KB | 1.2 KB |
# exit 0 at 2026-10-28T11:58:28Z load=1.01 1.03 1.27
