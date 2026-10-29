# 2026-10-28T12:25:33Z A=bin-b B=bin-c rounds=0 set=tablemem order=- nproc=4 load=4.87 3.81 3.04
A pairs order probe: "1,2,x"
B pairs order probe: "1,2,x"
| Shape | A bytes/table | B bytes/table |
|---|---:|---:|
| {} | 144 | 144 |
| 1 string key | 351 | 311 |
| 4 string keys | 495 | 455 |
| 16 string keys | 1407 | 1319 |
| 1 array item | 199 | 199 |
| 4 array items | 199 | 199 |
| 16 array items | 295 | 295 |
| { i, i + 1, x = i } (gc_small_tables) | 407 | 367 |
| 4 fields assigned after {} | 495 | 455 |
| array 4, then 1 string key | 407 | 367 |
| string key, then 4 array items | 407 | 367 |
| queue, 32 live after 1M push/pop (A) | 7688 bytes retained | |
| queue, 32 live after 1M push/pop (B) | 7032 bytes retained | |
# exit 0 at 2026-10-28T12:38:40Z load=2.25 2.85 3.00
