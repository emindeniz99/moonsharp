# 2026-10-28T11:58:28Z A=bin-master B=bin-b rounds=21 set=tables order=b nproc=4 load=1.01 1.03 1.27
A pairs order probe: "x,1,2"
B pairs order probe: "1,2,x"
A: bin-master/MoonSharp.Interpreter.dll
B: bin-b/MoonSharp.Interpreter.dll
21 interleaved rounds per workload, .NET 8.0.33

Table order (MOONSHARP_TABLE_ORDER): b
| Workload | A median | B median | B/A (median of rounds) | B/A interquartile range | A alloc/op | B alloc/op |
|---|---:|---:|---:|---:|---:|---:|
| table_array_insert | 590.3 ns | 211.5 ns | 0.376 | 0.321 to 0.391 | 380 B | 212 B |
| table_array_read | 379.0 ns | 409.6 ns | 0.971 | 0.930 to 1.063 | 528 B | 528 B |
| table_hash_set_get | 691.8 ns | 648.7 ns | 0.928 | 0.891 to 1.002 | 912 B | 864 B |
| gc_small_tables | 1.01 µs | 712.3 ns | 0.715 | 0.672 to 0.798 | 1.7 KB | 1.1 KB |
| table_sort_52 | 168.62 µs | 132.24 µs | 0.728 | 0.696 to 0.825 | 125.2 KB | 69.4 KB |
| table_concat | 1.12 µs | 932.7 ns | 0.855 | 0.825 to 0.904 | 1.4 KB | 1.4 KB |
| hash_delete_reinsert | 1.11 µs | 928.7 ns | 0.855 | 0.797 to 0.870 | 1.2 KB | 1.1 KB |
| queue_int_keys | 605.0 ns | 587.2 ns | 0.926 | 0.887 to 1.034 | 824 B | 709 B |
| pairs_mixed_128 | 426.7 ns | 417.9 ns | 1.000 | 0.949 to 1.022 | 601 B | 625 B |
| pairs_hash_128 | 413.3 ns | 407.7 ns | 0.990 | 0.940 to 0.997 | 601 B | 601 B |
| record_build_read | 1.35 µs | 935.5 ns | 0.712 | 0.648 to 0.722 | 1.8 KB | 1.2 KB |
# exit 0 at 2026-10-28T12:00:53Z load=1.98 1.38 1.36
