# 2026-10-28T13:41:36Z workloads 7 variant=b load=2.15 1.52 1.92
MoonSharp 3.0.0.0 on .NET 8.0.33, 7 runs per case (median, min, max ns per iteration)

| Case | Iterations | Median ns | Min ns | Max ns | Check | Note |
|---|---:|---:|---:|---:|---|---|
| empty_loop | 4000000 | 53.9 | 52.3 | 84.4 | void | MoonSharp prints a missing return value as void |
| function_call | 1000000 | 373.6 | 316.4 | 429.6 | 1000000 |  |
| method_call | 1000000 | 526.3 | 486.4 | 648.1 | 7100000 | check depends on prior runs |
| table_array_insert | 1000000 | 543.7 | 487.9 | 761.9 | 1000000 |  |
| table_array_read | 1000000 | 350.6 | 334.7 | 418.6 | 512370976 |  |
| table_hash_set_get | 1000000 | 622.6 | 593.3 | 896.1 | 500000500000 |  |
| string_concat | 500000 | 682.8 | 584.8 | 1028.2 | item499968 |  |
| string_format | 500000 | 4186.7 | 3835.0 | 4516.0 | 499968:x |  |
| closure_create_call | 1000000 | 684.7 | 590.7 | 908.5 | 500000500000 |  |
| int_math | 2000000 | 269.6 | 250.1 | 293.6 | 967568 |  |
| fib_recursive | 20000 | 185608.8 | 165752.7 | 1651926.1 | 2880000 |  |
| float_math | 2000000 | 372.6 | 350.5 | 409.1 | 0.004500 |  |
| string_pattern | 100000 | 23537.0 | 23346.4 | 24649.6 | 2500000 |  |
| table_sort_52 | 2000 | 96846.4 | 90052.9 | 131705.3 | 960000 | variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles |
| table_concat | 50000 | 978.6 | 943.6 | 1869.5 | 4300000 |  |
| coroutine_switch | 500000 | 869.0 | 670.0 | 928.1 | 125000250000 |  |
| gc_small_tables | 500000 | 735.6 | 658.5 | 784.0 | 125000750000 |  |
| oop_inherit | 1000000 | 433.4 | 430.7 | 473.1 | 3000000 |  |
# exit 0 at 2026-10-28T13:58:12Z load=1.25 1.27 1.51
