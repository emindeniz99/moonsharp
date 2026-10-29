# 2026-10-28T12:21:48Z A=bin-master B=bin-a rounds=0 set=tablemem order=- nproc=4 load=2.78 3.18 2.73
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
| Shape | A bytes/table | B bytes/table |
|---|---:|---:|
| {} | 264 | 144 |
| 1 string key | 583 | 351 |
| 4 string keys | 1007 | 495 |
| 16 string keys | 2535 | 1407 |
| 1 array item | 583 | 199 |
| 4 array items | 1007 | 199 |
| 16 array items | 2535 | 295 |
| { i, i + 1, x = i } (gc_small_tables) | 1007 | 407 |
| 4 fields assigned after {} | 1007 | 495 |
| array 4, then 1 string key | 1327 | 407 |
| string key, then 4 array items | 1327 | 519 |
| queue, 32 live after 1M push/pop (A) | 6584 bytes retained | |
| queue, 32 live after 1M push/pop (B) | 7688 bytes retained | |
# exit 0 at 2026-10-28T12:25:33Z load=4.87 3.81 3.04
