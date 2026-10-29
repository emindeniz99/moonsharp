# 2026-10-28T11:53:00Z A=bin-a B=bin-a rounds=11 set=tables order=a nproc=4 load=1.33 1.01 1.37
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
A: bin-a/MoonSharp.Interpreter.dll
B: bin-a/MoonSharp.Interpreter.dll
11 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): a
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 213.5 ns | 205.8 ns | 0.950 | 0.892 to 1.184 | 212 B | 212 B |
| table_array_read | 374.1 ns | 356.1 ns | 0.942 | 0.862 to 1.108 | 528 B | 528 B |
| table_hash_set_get | 670.1 ns | 633.8 ns | 0.990 | 0.894 to 1.125 | 864 B | 864 B |
| gc_small_tables | 729.2 ns | 733.3 ns | 0.990 | 0.929 to 1.031 | 1.1 KB | 1.1 KB |
| table_sort_52 | 175.32 µs | 149.93 µs | 0.998 | 0.808 to 1.059 | 69.4 KB | 69.4 KB |
| table_concat | 992.7 ns | 942.5 ns | 0.993 | 0.856 to 1.014 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.02 µs | 951.2 ns | 0.977 | 0.924 to 1.022 | 1.1 KB | 1.1 KB |
| queue_int_keys | 548.1 ns | 537.8 ns | 0.981 | 0.967 to 1.095 | 709 B | 709 B |
| pairs_mixed_128 | 431.1 ns | 405.3 ns | 0.932 | 0.908 to 0.975 | 625 B | 625 B |
| pairs_hash_128 | 426.1 ns | 445.4 ns | 1.009 | 0.897 to 1.064 | 601 B | 601 B |
| record_build_read | 1.03 µs | 951.3 ns | 0.949 | 0.829 to 1.075 | 1.2 KB | 1.2 KB |
# exit 0 at 2026-10-28T11:56:02Z load=1.11 1.05 1.32
