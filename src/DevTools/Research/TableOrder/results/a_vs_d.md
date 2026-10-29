# 2026-10-28T12:09:01Z A=bin-a B=bin-d rounds=21 set=tables order=a nproc=4 load=5.23 3.06 2.06
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
A: bin-a/MoonSharp.Interpreter.dll
B: bin-d/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): a
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 192.0 ns | 202.3 ns | 1.023 | 0.980 to 1.086 | 212 B | 212 B |
| table_array_read | 359.3 ns | 354.6 ns | 0.982 | 0.939 to 1.007 | 528 B | 528 B |
| table_hash_set_get | 655.8 ns | 669.4 ns | 1.027 | 0.980 to 1.054 | 864 B | 864 B |
| gc_small_tables | 751.5 ns | 749.7 ns | 1.010 | 0.947 to 1.051 | 1.1 KB | 1.1 KB |
| table_sort_52 | 137.69 µs | 139.03 µs | 0.975 | 0.844 to 1.021 | 69.4 KB | 69.4 KB |
| table_concat | 988.4 ns | 1.01 µs | 0.992 | 0.972 to 1.105 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 999.2 ns | 1.00 µs | 1.002 | 0.929 to 1.031 | 1.1 KB | 1.1 KB |
| queue_int_keys | 557.8 ns | 565.6 ns | 1.006 | 0.987 to 1.061 | 709 B | 709 B |
| pairs_mixed_128 | 426.8 ns | 413.4 ns | 0.985 | 0.968 to 1.039 | 625 B | 625 B |
| pairs_hash_128 | 432.1 ns | 437.3 ns | 0.997 | 0.948 to 1.065 | 601 B | 601 B |
| record_build_read | 909.2 ns | 961.8 ns | 1.023 | 0.972 to 1.107 | 1.2 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:12:25Z load=1.68 2.35 1.97
