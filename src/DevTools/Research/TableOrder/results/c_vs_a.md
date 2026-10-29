# 2026-10-28T12:03:21Z A=bin-c B=bin-a rounds=21 set=tables order=- nproc=4 load=2.49 1.88 1.55
A pairs order probe: "1,2,x"
B pairs order probe: "x,1,2"
A: bin-c/MoonSharp.Interpreter.dll
B: bin-a/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): (unset)
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 214.2 ns | 202.1 ns | 0.995 | 0.890 to 1.059 | 212 B | 212 B |
| table_array_read | 361.4 ns | 347.6 ns | 1.016 | 0.951 to 1.034 | 528 B | 528 B |
| table_hash_set_get | 705.2 ns | 668.0 ns | 0.998 | 0.861 to 1.024 | 864 B | 864 B |
| gc_small_tables | 748.5 ns | 729.5 ns | 1.003 | 0.917 to 1.078 | 1.1 KB | 1.1 KB |
| table_sort_52 | 141.92 µs | 139.81 µs | 1.027 | 0.963 to 1.114 | 69.4 KB | 69.4 KB |
| table_concat | 935.3 ns | 905.3 ns | 0.965 | 0.938 to 0.982 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.51 µs | 922.6 ns | 0.613 | 0.590 to 0.632 | 1.7 KB | 1.1 KB |
| queue_int_keys | 537.9 ns | 572.3 ns | 1.041 | 1.011 to 1.085 | 672 B | 709 B |
| pairs_mixed_128 | 407.2 ns | 406.7 ns | 1.001 | 0.919 to 1.019 | 625 B | 625 B |
| pairs_hash_128 | 413.6 ns | 423.9 ns | 1.004 | 0.984 to 1.056 | 601 B | 601 B |
| record_build_read | 874.5 ns | 911.0 ns | 1.022 | 1.010 to 1.111 | 1.1 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:05:36Z load=1.47 1.73 1.54
