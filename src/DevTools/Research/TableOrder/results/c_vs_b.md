# 2026-10-28T12:05:36Z A=bin-c B=bin-b rounds=21 set=tables order=- nproc=4 load=1.47 1.73 1.54
A pairs order probe: "1,2,x"
B pairs order probe: "1,2,x"
A: bin-c/MoonSharp.Interpreter.dll
B: bin-b/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): (unset)
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 175.3 ns | 188.6 ns | 1.064 | 1.023 to 1.125 | 212 B | 212 B |
| table_array_read | 359.3 ns | 342.7 ns | 1.002 | 0.957 to 1.028 | 528 B | 528 B |
| table_hash_set_get | 631.4 ns | 623.5 ns | 1.005 | 0.962 to 1.059 | 864 B | 864 B |
| gc_small_tables | 679.7 ns | 692.2 ns | 1.014 | 0.985 to 1.045 | 1.1 KB | 1.1 KB |
| table_sort_52 | 94.04 µs | 133.85 µs | 1.075 | 0.993 to 1.117 | 69.4 KB | 69.4 KB |
| table_concat | 895.2 ns | 901.4 ns | 0.986 | 0.973 to 1.002 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.58 µs | 972.9 ns | 0.600 | 0.575 to 0.632 | 1.8 KB | 1.1 KB |
| queue_int_keys | 553.3 ns | 569.1 ns | 1.034 | 0.962 to 1.119 | 672 B | 709 B |
| pairs_mixed_128 | 430.6 ns | 448.7 ns | 1.063 | 0.964 to 1.093 | 625 B | 625 B |
| pairs_hash_128 | 410.1 ns | 439.4 ns | 1.062 | 0.991 to 1.118 | 601 B | 601 B |
| record_build_read | 871.3 ns | 864.8 ns | 0.991 | 0.958 to 1.078 | 1.1 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:09:01Z load=5.23 3.06 2.06
