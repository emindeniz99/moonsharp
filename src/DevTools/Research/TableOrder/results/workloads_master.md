# 2026-10-28T12:38:40Z workloads 7 variant=master load=2.25 2.85 3.00
MoonSharp 3.0.0.0 on .NET 8.0.33, 7 runs per case (median, min, max ns per iteration)

| Case | Iterations | Median ns | Min ns | Max ns | Check | Note |
|---|---:|---:|---:|---:|---|---|
| empty_loop | 4000000 | 56.8 | 53.6 | 91.9 | void | MoonSharp prints a missing return value as void |
| function_call | 1000000 | 328.3 | 309.6 | 427.8 | 1000000 |  |
| method_call | 1000000 | 584.9 | 553.7 | 635.5 | 7100000 | check depends on prior runs |
| table_array_insert | 1000000 | 1166.5 | 915.6 | 1384.4 | 1000000 |  |
| table_array_read | 1000000 | 353.5 | 346.5 | 761.9 | 512370976 |  |
| table_hash_set_get | 1000000 | 670.0 | 638.9 | 740.3 | 500000500000 |  |
| string_concat | 500000 | 676.7 | 566.0 | 720.1 | item499968 |  |
| string_format | 500000 | 3993.7 | 3897.0 | 4131.8 | 499968:x |  |
| closure_create_call | 1000000 | 620.7 | 588.6 | 681.6 | 500000500000 |  |
| int_math | 2000000 | 250.6 | 239.8 | 275.2 | 967568 |  |
| fib_recursive | 20000 | 173466.6 | 172334.8 | 182405.7 | 2880000 |  |
| float_math | 2000000 | 386.3 | 368.8 | 406.3 | 0.004500 |  |
| string_pattern | 100000 | 23566.9 | 23052.5 | 24512.9 | 2500000 |  |
| table_sort_52 | 2000 | 140173.6 | 128784.5 | 172279.9 | 960000 | variant: copy loop instead of table.move; the seed arithmetic exceeds 2^53 under doubles |
| table_concat | 50000 | 1159.6 | 1076.2 | 1914.4 | 4300000 |  |
| coroutine_switch | 500000 | 784.1 | 705.0 | 889.2 | 125000250000 |  |
| gc_small_tables | 500000 | 1118.6 | 978.1 | 1367.6 | 125000750000 |  |
| oop_inherit | 1000000 | 495.2 | 488.3 | 527.7 | 3000000 |  |
# exit 0 at 2026-10-28T12:53:01Z load=1.13 1.88 2.56
