rounds per series: {'workload/empty_loop/luacs': 3, 'workload/empty_loop/msA': 3, 'workload/empty_loop/msB': 3}

| kind | case | msA ns med (IQR) | msB ns med (IQR) | luacs ns med (IQR) | msA/luacs | msA/msB (A/A) | A/A per-round ratio range |
|---|---|---:|---:|---:|---:|---:|---|
| workload | empty_loop | 60.2 (56.1–63.8) | 57.7 (57.0–75.5) | 5.3 (5.3–5.6) | 11.30x | 1.042 | 0.845–1.042 |
| workload | function_call | 372.0 (365.0–419.3) | 385.9 (370.9–459.4) | 95.8 (90.7–101.2) | 3.88x | 0.964 | 0.913–1.003 |
| workload | method_call | 773.1 (733.4–852.8) | 726.1 (718.2–838.3) | 184.8 (169.8–189.8) | 4.18x | 1.065 | 1.017–1.065 |
| workload | table_array_insert | 1,224 (1,091–1,307) | 1,267 (1,203–1,409) | 71.0 (63.2–81.2) | 17.24x | 0.966 | 0.869–1.031 |
| workload | table_array_read | 464.8 (457.2–534.9) | 478.3 (426.3–701.7) | 39.3 (38.1–48.2) | 11.81x | 0.972 | 0.762–1.090 |
| workload | table_hash_set_get | 806.3 (792.7–815.4) | 853.6 (759.2–1,107) | 94.6 (92.2–107.4) | 8.53x | 0.945 | 0.737–1.062 |
| workload | string_concat | 679.1 (670.9–824.2) | 705.5 (649.8–765.6) | 316.4 (308.1–471.1) | 2.15x | 0.963 | 0.963–1.076 |
| workload | string_format | 5,241 (5,172–5,503) | 5,309 (5,006–5,966) | 333.4 (326.2–364.2) | 15.72x | 0.987 | 0.922–1.047 |
| workload | closure_create_call | 938.2 (933.1–971.7) | 1,000 (944.7–1,039) | 216.7 (212.2–218.2) | 4.33x | 0.938 | 0.935–0.988 |
| workload | int_math | 310.4 (309.3–325.4) | 318.1 (302.8–439.9) | 32.5 (31.8–35.5) | 9.54x | 0.976 | 0.706–1.023 |
| workload | fib_recursive | 206,643 (200,848–214,030) | 210,561 (208,021–225,565) | 52,226 (50,029–54,666) | 3.96x | 0.981 | 0.949–0.993 |
| workload | float_math | 495.0 (492.3–513.3) | 501.5 (488.1–505.8) | 52.4 (47.8–56.6) | 9.44x | 0.987 | 0.973–1.024 |
| workload | string_pattern | 26,704 (26,576–26,892) | 27,140 (26,726–27,555) | 9,566 (8,826–9,753) | 2.79x | 0.984 | 0.976–0.994 |
| workload | table_sort_52 | 145,231 (140,966–158,625) | 151,206 (147,065–178,204) | 81,056 (79,529–107,293) | 1.79x | 0.960 | 0.890–0.960 |
| workload | table_concat | 1,408 (1,394–1,447) | 1,361 (1,322–1,717) | 1,286 (1,145–1,797) | 1.09x | 1.035 | 0.842–1.065 |
| workload | coroutine_switch | 799.4 (782.0–810.7) | 857.9 (842.7–859.0) | 983.9 (851.7–1,363) | 0.81x | 0.932 | 0.928–0.944 |
| workload | gc_small_tables | 1,299 (1,281–1,551) | 1,301 (1,277–1,317) | 368.8 (305.4–510.9) | 3.52x | 0.999 | 0.972–1.192 |
| workload | oop_inherit | 664.0 (657.4–735.0) | 697.1 (637.8–741.6) | 215.5 (192.4–344.1) | 3.08x | 0.953 | 0.943–1.041 |

| Workload | native Lua 5.5.1 (doc) | MoonSharp | Lua-CSharp 0.5.8 | MoonSharp/Lua-CSharp | Lua-CSharp/native | MoonSharp/native |
|---|---:|---:|---:|---:|---:|---:|
| empty_loop | 3.5 | 60.2 (56.1–63.8) | 5.3 (5.3–5.6) | 11.3x | 1.5x | 17.2x |
| function_call | 26.0 | 372.0 (365.0–419.3) | 95.8 (90.7–101.2) | 3.9x | 3.7x | 14.3x |
| method_call | 66.3 | 773.1 (733.4–852.8) | 184.8 (169.8–189.8) | 4.2x | 2.8x | 11.7x |
| table_array_insert | 16.1 | 1,224 (1,091–1,307) | 71.0 (63.2–81.2) | 17.2x | 4.4x | 76.0x |
| table_array_read | 15.7 | 464.8 (457.2–534.9) | 39.3 (38.1–48.2) | 11.8x | 2.5x | 29.6x |
| table_hash_set_get | 35.8 | 806.3 (792.7–815.4) | 94.6 (92.2–107.4) | 8.5x | 2.6x | 22.5x |
| string_concat | 225.8 | 679.1 (670.9–824.2) | 316.4 (308.1–471.1) | 2.1x | 1.4x | 3.0x |
| string_format | 304.8 | 5,241 (5,172–5,503) | 333.4 (326.2–364.2) | 15.7x | 1.1x | 17.2x |
| closure_create_call | 94.0 | 938.2 (933.1–971.7) | 216.7 (212.2–218.2) | 4.3x | 2.3x | 10.0x |
| int_math | 11.3 | 310.4 (309.3–325.4) | 32.5 (31.8–35.5) | 9.5x | 2.9x | 27.5x |
| fib_recursive | 10,085 | 206,643 (200,848–214,030) | 52,226 (50,029–54,666) | 4.0x | 5.2x | 20.5x |
| float_math | 20.8 | 495.0 (492.3–513.3) | 52.4 (47.8–56.6) | 9.4x | 2.5x | 23.8x |
| string_pattern | 3,199 | 26,704 (26,576–26,892) | 9,566 (8,826–9,753) | 2.8x | 3.0x | 8.3x |
| table_sort_52 | 51,009 | 145,231 (140,966–158,625) | 81,056 (79,529–107,293) | 1.8x | 1.6x | 2.8x |
| table_concat | 869.2 | 1,408 (1,394–1,447) | 1,286 (1,145–1,797) | 1.1x | 1.5x | 1.6x |
| coroutine_switch | 118.9 | 799.4 (782.0–810.7) | 983.9 (851.7–1,363) | 0.8x | 8.3x | 6.7x |
| gc_small_tables | 128.8 | 1,299 (1,281–1,551) | 368.8 (305.4–510.9) | 3.5x | 2.9x | 10.1x |
| oop_inherit | 69.6 | 664.0 (657.4–735.0) | 215.5 (192.4–344.1) | 3.1x | 3.1x | 9.5x |

| kind | case | label | alloc B/op thread (median of rounds) | alloc B/op process | rounds |
|---|---|---|---:|---:|---:|
| workload | closure_create_call | luacs | 200.00 | 200.00 | 3 |
| workload | closure_create_call | msA | 912.00 | 912.00 | 3 |
| workload | closure_create_call | msB | 912.00 | 912.00 | 3 |
| workload | coroutine_switch | luacs | 0.02 | 0.02 | 3 |
| workload | coroutine_switch | msA | 1,080.01 | 1,080.01 | 3 |
| workload | coroutine_switch | msB | 1,080.01 | 1,080.01 | 3 |
| workload | empty_loop | luacs | 0.00 | 0.00 | 3 |
| workload | empty_loop | msA | 48.00 | 48.00 | 3 |
| workload | empty_loop | msB | 48.00 | 48.00 | 3 |
| workload | fib_recursive | luacs | 0.01 | 0.01 | 3 |
| workload | fib_recursive | msA | 301,368.04 | 301,368.04 | 3 |
| workload | fib_recursive | msB | 301,368.04 | 301,368.04 | 3 |
| workload | float_math | luacs | 0.00 | 0.00 | 3 |
| workload | float_math | msA | 576.00 | 576.00 | 3 |
| workload | float_math | msB | 576.00 | 576.00 | 3 |
| workload | function_call | luacs | 0.00 | 0.00 | 3 |
| workload | function_call | msA | 552.00 | 552.00 | 3 |
| workload | function_call | msB | 552.00 | 552.00 | 3 |
| workload | gc_small_tables | luacs | 704.01 | 704.01 | 3 |
| workload | gc_small_tables | msA | 1,776.04 | 1,776.04 | 3 |
| workload | gc_small_tables | msB | 1,776.04 | 1,776.04 | 3 |
| workload | int_math | luacs | 0.00 | 0.00 | 3 |
| workload | int_math | msA | 288.00 | 288.00 | 3 |
| workload | int_math | msB | 288.00 | 288.00 | 3 |
| workload | method_call | luacs | 0.00 | 0.00 | 3 |
| workload | method_call | msA | 696.00 | 696.00 | 3 |
| workload | method_call | msB | 696.00 | 696.00 | 3 |
| workload | oop_inherit | luacs | 0.00 | 0.00 | 3 |
| workload | oop_inherit | msA | 600.00 | 600.00 | 3 |
| workload | oop_inherit | msB | 600.00 | 600.00 | 3 |
| workload | string_concat | luacs | 84.81 | 84.81 | 3 |
| workload | string_concat | msA | 660.82 | 660.82 | 3 |
| workload | string_concat | msB | 660.82 | 660.82 | 3 |
| workload | string_format | luacs | 174.37 | 174.37 | 3 |
| workload | string_format | msA | 7,999.83 | 7,999.83 | 3 |
| workload | string_format | msB | 7,999.83 | 7,999.83 | 3 |
| workload | string_pattern | luacs | 1,680.00 | 1,680.00 | 3 |
| workload | string_pattern | msA | 49,856.01 | 49,856.01 | 3 |
| workload | string_pattern | msB | 49,856.01 | 49,856.01 | 3 |
| workload | table_array_insert | luacs | 50.33 | 50.33 | 3 |
| workload | table_array_insert | msA | 371.44 | 371.45 | 3 |
| workload | table_array_insert | msB | 371.44 | 371.45 | 3 |
| workload | table_array_read | luacs | 0.00 | 0.00 | 3 |
| workload | table_array_read | msA | 528.00 | 528.00 | 3 |
| workload | table_array_read | msB | 528.00 | 528.00 | 3 |
| workload | table_concat | luacs | 200.00 | 200.00 | 3 |
| workload | table_concat | msA | 1,392.02 | 1,392.02 | 3 |
| workload | table_concat | msB | 1,392.02 | 1,392.02 | 3 |
| workload | table_hash_set_get | luacs | 0.01 | 0.01 | 3 |
| workload | table_hash_set_get | msA | 912.01 | 912.01 | 3 |
| workload | table_hash_set_get | msB | 912.01 | 912.01 | 3 |
| workload | table_sort_52 | luacs | 12,344.06 | 12,344.06 | 3 |
| workload | table_sort_52 | msA | 128,128.41 | 128,128.41 | 3 |
| workload | table_sort_52 | msB | 128,128.41 | 128,128.41 | 3 |

retained bytes per instance (median of rounds, min-max):

distinct checks:
  ('workload', 'closure_create_call', 'luacs') ['500000500000']
  ('workload', 'closure_create_call', 'msA') ['500000500000']
  ('workload', 'closure_create_call', 'msB') ['500000500000']
  ('workload', 'coroutine_switch', 'luacs') ['125000250000']
  ('workload', 'coroutine_switch', 'msA') ['125000250000']
  ('workload', 'coroutine_switch', 'msB') ['125000250000']
  ('workload', 'empty_loop', 'luacs') ['void']
  ('workload', 'empty_loop', 'msA') ['void']
  ('workload', 'empty_loop', 'msB') ['void']
  ('workload', 'fib_recursive', 'luacs') ['2880000']
  ('workload', 'fib_recursive', 'msA') ['2880000']
  ('workload', 'fib_recursive', 'msB') ['2880000']
  ('workload', 'float_math', 'luacs') ['0.004500']
  ('workload', 'float_math', 'msA') ['0.004500']
  ('workload', 'float_math', 'msB') ['0.004500']
  ('workload', 'function_call', 'luacs') ['1000000']
  ('workload', 'function_call', 'msA') ['1000000']
  ('workload', 'function_call', 'msB') ['1000000']
  ('workload', 'gc_small_tables', 'luacs') ['125000750000']
  ('workload', 'gc_small_tables', 'msA') ['125000750000']
  ('workload', 'gc_small_tables', 'msB') ['125000750000']
  ('workload', 'int_math', 'luacs') ['967568']
  ('workload', 'int_math', 'msA') ['967568']
  ('workload', 'int_math', 'msB') ['967568']
  ('workload', 'method_call', 'luacs') ['7100000']
  ('workload', 'method_call', 'msA') ['7100000']
  ('workload', 'method_call', 'msB') ['7100000']
  ('workload', 'oop_inherit', 'luacs') ['3000000']
  ('workload', 'oop_inherit', 'msA') ['3000000']
  ('workload', 'oop_inherit', 'msB') ['3000000']
  ('workload', 'string_concat', 'luacs') ['item499968']
  ('workload', 'string_concat', 'msA') ['item499968']
  ('workload', 'string_concat', 'msB') ['item499968']
  ('workload', 'string_format', 'luacs') ['499968:x']
  ('workload', 'string_format', 'msA') ['499968:x']
  ('workload', 'string_format', 'msB') ['499968:x']
  ('workload', 'string_pattern', 'luacs') ['2500000']
  ('workload', 'string_pattern', 'msA') ['2500000']
  ('workload', 'string_pattern', 'msB') ['2500000']
  ('workload', 'table_array_insert', 'luacs') ['1000000']
  ('workload', 'table_array_insert', 'msA') ['1000000']
  ('workload', 'table_array_insert', 'msB') ['1000000']
  ('workload', 'table_array_read', 'luacs') ['512370976']
  ('workload', 'table_array_read', 'msA') ['512370976']
  ('workload', 'table_array_read', 'msB') ['512370976']
  ('workload', 'table_concat', 'luacs') ['4300000']
  ('workload', 'table_concat', 'msA') ['4300000']
  ('workload', 'table_concat', 'msB') ['4300000']
  ('workload', 'table_hash_set_get', 'luacs') ['500000500000']
  ('workload', 'table_hash_set_get', 'msA') ['500000500000']
  ('workload', 'table_hash_set_get', 'msB') ['500000500000']
  ('workload', 'table_sort_52', 'luacs') ['960000']
  ('workload', 'table_sort_52', 'msA') ['960000']
  ('workload', 'table_sort_52', 'msB') ['960000']

failures: []
ValueTask completions (sync, async): {'luacs': [486, 0], 'msA': [0, 0], 'msB': [0, 0]}
