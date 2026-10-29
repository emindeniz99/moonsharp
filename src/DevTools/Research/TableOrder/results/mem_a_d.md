# 2026-10-28T13:20:49Z A=bin-a B=bin-d rounds=0 set=tablemem order=a nproc=4 load=3.41 3.07 2.98
A pairs order probe: "x,1,2"
B pairs order probe: "x,1,2"
| Shape | A bytes/table | B bytes/table |
|---|---:|---:|
| {} | 144 | 152 |
| 1 string key | 351 | 359 |
| 4 string keys | 495 | 503 |
| 16 string keys | 1407 | 1415 |
| 1 array item | 199 | 207 |
| 4 array items | 199 | 207 |
| 16 array items | 295 | 303 |
| { i, i + 1, x = i } (gc_small_tables) | 407 | 415 |
| 4 fields assigned after {} | 495 | 503 |
| array 4, then 1 string key | 407 | 415 |
| string key, then 4 array items | 519 | 527 |
| queue, 32 live after 1M push/pop (A) | 7688 bytes retained | |
| queue, 32 live after 1M push/pop (B) | 7696 bytes retained | |
# exit 0 at 2026-10-28T13:42:19Z load=1.54 1.44 1.87
