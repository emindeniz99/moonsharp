# 2026-10-28T12:12:25Z A=bin-c B=bin-d rounds=21 set=tables order=c nproc=4 load=1.68 2.35 1.97
A pairs order probe: "1,2,x"
B pairs order probe: "1,2,x"
A: bin-c/MoonSharp.Interpreter.dll
B: bin-d/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): c
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 233.6 ns | 216.7 ns | 0.976 | 0.832 to 1.046 | 212 B | 212 B |
| table_array_read | 372.7 ns | 358.8 ns | 0.984 | 0.940 to 1.038 | 528 B | 528 B |
| table_hash_set_get | 694.2 ns | 680.9 ns | 0.930 | 0.891 to 1.020 | 864 B | 864 B |
| gc_small_tables | 720.7 ns | 722.0 ns | 1.020 | 0.967 to 1.052 | 1.1 KB | 1.1 KB |
| table_sort_52 | 121.12 µs | 123.88 µs | 1.012 | 0.983 to 1.048 | 69.4 KB | 69.4 KB |
| table_concat | 911.5 ns | 964.3 ns | 1.044 | 1.028 to 1.070 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.59 µs | 1.64 µs | 1.029 | 0.974 to 1.063 | 1.8 KB | 1.8 KB |
| queue_int_keys | 542.2 ns | 554.1 ns | 1.026 | 0.939 to 1.075 | 672 B | 672 B |
| pairs_mixed_128 | 411.2 ns | 425.0 ns | 1.010 | 0.991 to 1.094 | 625 B | 625 B |
| pairs_hash_128 | 429.9 ns | 422.2 ns | 0.975 | 0.949 to 1.039 | 601 B | 601 B |
| record_build_read | 883.8 ns | 900.7 ns | 1.005 | 0.973 to 1.062 | 1.1 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:15:25Z load=3.16 3.44 2.52
