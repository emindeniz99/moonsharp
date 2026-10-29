# 2026-10-28T11:37:44Z A=bin-master B=bin-master rounds=11 set=tables order=- nproc=4 load=1.22 1.41 2.10
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
A: bin-master/MoonSharp.Interpreter.dll
B: bin-master/MoonSharp.Interpreter.dll
11 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): (unset)
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 540.5 ns | 542.5 ns | 1.007 | 0.961 to 1.050 | 380 B | 380 B |
| table_array_read | 411.1 ns | 399.5 ns | 1.010 | 0.937 to 1.115 | 528 B | 528 B |
| table_hash_set_get | 782.5 ns | 688.4 ns | 0.924 | 0.866 to 1.039 | 912 B | 912 B |
| gc_small_tables | 996.2 ns | 1.01 µs | 1.021 | 0.982 to 1.049 | 1.7 KB | 1.7 KB |
| table_sort_52 | 173.15 µs | 177.93 µs | 1.015 | 1.001 to 1.131 | 125.2 KB | 125.2 KB |
| table_concat | 1.07 µs | 1.07 µs | 0.987 | 0.872 to 1.013 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.09 µs | 1.15 µs | 1.022 | 0.965 to 1.127 | 1.2 KB | 1.2 KB |
| queue_int_keys | 660.5 ns | 624.1 ns | 0.959 | 0.912 to 1.018 | 824 B | 824 B |
| pairs_mixed_128 | 433.8 ns | 438.4 ns | 1.016 | 0.972 to 1.116 | 601 B | 601 B |
| pairs_hash_128 | 419.1 ns | 465.9 ns | 1.052 | 1.012 to 1.246 | 601 B | 601 B |
| record_build_read | 1.36 µs | 1.28 µs | 0.994 | 0.879 to 1.098 | 1.8 KB | 1.8 KB |
# exit 0 at 2026-10-28T11:53:00Z load=1.33 1.01 1.37
