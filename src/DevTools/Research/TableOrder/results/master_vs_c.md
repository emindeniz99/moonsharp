# 2026-10-28T12:00:53Z A=bin-master B=bin-c rounds=21 set=tables order=c nproc=4 load=1.98 1.38 1.36
A pairs order probe: "x,1,2"
B pairs order probe: "1,2,x"
A: bin-master/MoonSharp.Interpreter.dll
B: bin-c/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): c
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 546.2 ns | 238.4 ns | 0.441 | 0.390 to 0.473 | 380 B | 212 B |
| table_array_read | 367.5 ns | 350.7 ns | 0.973 | 0.918 to 1.058 | 528 B | 528 B |
| table_hash_set_get | 652.3 ns | 649.2 ns | 0.947 | 0.880 to 1.088 | 912 B | 864 B |
| gc_small_tables | 1.05 µs | 784.9 ns | 0.731 | 0.684 to 0.794 | 1.7 KB | 1.1 KB |
| table_sort_52 | 170.37 µs | 143.64 µs | 0.835 | 0.737 to 0.881 | 125.2 KB | 69.4 KB |
| table_concat | 1.16 µs | 1.11 µs | 0.904 | 0.837 to 0.919 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.07 µs | 1.58 µs | 1.475 | 1.371 to 1.564 | 1.2 KB | 1.8 KB |
| queue_int_keys | 579.3 ns | 521.3 ns | 0.896 | 0.851 to 0.921 | 824 B | 672 B |
| pairs_mixed_128 | 396.6 ns | 395.5 ns | 1.015 | 0.993 to 1.054 | 601 B | 625 B |
| pairs_hash_128 | 419.9 ns | 422.9 ns | 1.009 | 0.943 to 1.044 | 601 B | 601 B |
| record_build_read | 1.28 µs | 877.0 ns | 0.689 | 0.627 to 0.713 | 1.8 KB | 1.1 KB |
# exit 0 at 2026-10-28T12:03:21Z load=2.49 1.88 1.55
