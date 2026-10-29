# 2026-10-28T13:58:12Z workloads 7 variant=c load=1.25 1.27 1.51
MoonSharp 3.0.0.0 on .NET 8.0.33, 7 runs per case (median, min, max ns per iteration)

| Case | Iterations | Median ns | Min ns | Max ns | Check | Note |
|---|---:|---:|---:|---:|---|---|
| empty_loop | 4000000 | 49.9 | 49.5 | 80.3 | void | MoonSharp prints a missing return value as void |
| function_call | 1000000 | 379.5 | 314.3 | 445.4 | 1000000 |  |
| method_call | 1000000 | 620.1 | 507.7 | 642.2 | 7100000 | check depends on prior runs |
| table_array_insert | 1000000 | 639.8 | 399.2 | 862.8 | 1000000 |  |
| table_array_read | 1000000 | 385.0 | 330.1 | 621.4 | 512370976 |  |
| table_hash_set_get | 1000000 | 652.5 | 591.7 | 755.7 | 500000500000 |  |
| string_concat | 500000 | 515.0 | 502.3 | 605.6 | item499968 |  |
| string_format | 500000 | 3820.8 | 3545.8 | 4792.5 | 499968:x |  |
| closure_create_call | 1000000 | 618.0 | 575.3 | 706.1 | 500000500000 |  |
| int_math | 2000000 | 229.5 | 220.0 | 257.3 | 967568 |  |
| fib_recursive | 20000 | 171574.0 | 160374.8 | 178063.8 | 2880000 |  |
| float_math | 2000000 | 359.7 | 348.7 | 387.2 | 0.004500 |  |
| string_pattern | 100000 | 26091.1 | 22335.1 | 32073.7 | 2500000 |  |
| table_sort_52 | 2000 | 98673.4 | 92807.3 | 199459.1 | 960000 | variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles |
| table_concat | 50000 | 1099.0 | 931.9 | 2181.0 | 4300000 |  |
| coroutine_switch | 500000 | 762.4 | 674.2 | 847.9 | 125000250000 |  |
| gc_small_tables | 500000 | 718.9 | 696.5 | 822.6 | 125000750000 |  |
| oop_inherit | 1000000 | 444.9 | 435.0 | 517.5 | 3000000 |  |
# exit 0 at 2026-10-28T14:15:14Z load=6.19 3.44 2.73
