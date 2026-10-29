# 2026-10-28T13:10:17Z A=bin-2c9e546 B=bin-a rounds=0 set=tablemem order=- nproc=4 load=2.98 3.85 3.21
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
| Shape | A bytes/table | B bytes/table |
|---|---:|---:|
| {} | 136 | 144 |
| 1 string key | 343 | 351 |
| 4 string keys | 487 | 495 |
| 16 string keys | 1399 | 1407 |
| 1 array item | 191 | 199 |
| 4 array items | 191 | 199 |
| 16 array items | 287 | 295 |
| { i, i + 1, x = i } (gc_small_tables) | 399 | 407 |
| 4 fields assigned after {} | 487 | 495 |
| array 4, then 1 string key | 399 | 407 |
| string key, then 4 array items | 511 | 519 |
| queue, 32 live after 1M push/pop (A) | 8390488 bytes retained | |
| queue, 32 live after 1M push/pop (B) | 7688 bytes retained | |
# exit 0 at 2026-10-28T13:36:20Z load=1.02 1.52 2.13
