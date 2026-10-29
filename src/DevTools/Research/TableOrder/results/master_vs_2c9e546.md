# 2026-10-28T12:15:25Z A=bin-master B=bin-2c9e546 rounds=21 set=tables order=- nproc=4 load=3.16 3.44 2.52
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
A: bin-master/MoonSharp.Interpreter.dll
B: bin-2c9e546/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): (unset)
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 530.1 ns | 198.1 ns | 0.382 | 0.359 to 0.406 | 380 B | 212 B |
| table_array_read | 345.2 ns | 346.0 ns | 1.013 | 1.006 to 1.022 | 528 B | 528 B |
| table_hash_set_get | 645.8 ns | 646.3 ns | 0.972 | 0.954 to 1.040 | 912 B | 864 B |
| gc_small_tables | 1.07 µs | 748.4 ns | 0.706 | 0.683 to 0.732 | 1.7 KB | 1.1 KB |
| table_sort_52 | 124.54 µs | 88.54 µs | 0.724 | 0.701 to 0.821 | 125.2 KB | 69.4 KB |
| table_concat | 1.09 µs | 900.6 ns | 0.834 | 0.809 to 0.850 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.09 µs | 878.2 ns | 0.801 | 0.778 to 0.858 | 1.2 KB | 1.0 KB |
| queue_int_keys | 618.2 ns | 636.1 ns | 1.032 | 0.974 to 1.091 | 824 B | 693 B |
| pairs_mixed_128 | 421.7 ns | 430.8 ns | 1.021 | 0.948 to 1.084 | 601 B | 625 B |
| pairs_hash_128 | 434.9 ns | 442.0 ns | 1.028 | 0.867 to 1.075 | 601 B | 601 B |
| record_build_read | 1.31 µs | 911.1 ns | 0.721 | 0.683 to 0.757 | 1.8 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:21:48Z load=2.78 3.18 2.73
