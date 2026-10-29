# 2026-10-28T12:53:01Z workloads 7 variant=a load=1.13 1.88 2.56
MoonSharp 3.0.0.0 on .NET 8.0.33, 7 runs per case (median, min, max ns per iteration)

| Case | Iterations | Median ns | Min ns | Max ns | Check | Note |
|---|---:|---:|---:|---:|---|---|
| empty_loop | 4000000 | 64.4 | 52.9 | 82.3 | void | MoonSharp prints a missing return value as void |
| function_call | 1000000 | 319.5 | 314.4 | 504.8 | 1000000 |  |
| method_call | 1000000 | 514.1 | 488.9 | 645.7 | 7100000 | check depends on prior runs |
| table_array_insert | 1000000 | 582.8 | 329.9 | 812.1 | 1000000 |  |
| table_array_read | 1000000 | 347.8 | 342.8 | 453.2 | 512370976 |  |
| table_hash_set_get | 1000000 | 674.0 | 602.1 | 740.7 | 500000500000 |  |
| string_concat | 500000 | 517.6 | 500.7 | 632.6 | item499968 |  |
| string_format | 500000 | 3890.1 | 3741.5 | 4230.4 | 499968:x |  |
| closure_create_call | 1000000 | 592.3 | 575.9 | 738.1 | 500000500000 |  |
| int_math | 2000000 | 235.1 | 228.0 | 253.9 | 967568 |  |
| fib_recursive | 20000 | 179378.4 | 164972.3 | 188152.7 | 2880000 |  |
| float_math | 2000000 | 398.5 | 358.1 | 426.5 | 0.004500 |  |
| string_pattern | 100000 | 23851.6 | 22975.9 | 24906.9 | 2500000 |  |
| table_sort_52 | 2000 | 95409.2 | 88264.4 | 169275.7 | 960000 | variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles |
| table_concat | 50000 | 947.8 | 939.8 | 1820.3 | 4300000 |  |
| coroutine_switch | 500000 | 719.8 | 678.9 | 876.4 | 125000250000 |  |
| gc_small_tables | 500000 | 759.3 | 680.7 | 844.1 | 125000750000 |  |
| oop_inherit | 1000000 | 517.8 | 490.5 | 670.2 | 3000000 |  |
# exit 0 at 2026-10-28T13:18:16Z load=3.90 3.00 2.95
