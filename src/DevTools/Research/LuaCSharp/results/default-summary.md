rounds per series: {'boundary/a_cs_to_lua_call3/luacs': 7, 'boundary/a_cs_to_lua_call3/msA': 7, 'boundary/a_cs_to_lua_call3/msB': 7, 'create/c_create_stdlib/luacs': 7, 'create/c_create_default/msA': 7, 'create/c_create_default/msB': 7, 'create/c_create_complete/mscomplete': 7, 'workload/empty_loop/luacs': 7, 'workload/empty_loop/msA': 7, 'workload/empty_loop/msB': 7}

| kind | case | msA ns med (IQR) | msB ns med (IQR) | luacs ns med (IQR) | msA/luacs | msA/msB (A/A) | A/A per-round ratio range |
|---|---|---:|---:|---:|---:|---:|---|
| boundary | a_cs_to_lua_call3 | 491.7 (485.5–522.6) | 498.7 (475.2–549.8) | 338.1 (332.2–343.1) | 1.45x | 0.986 | 0.885–1.197 |
| boundary | a_cs_to_lua_call3_stackapi | n/a | n/a | 124.6 (121.6–140.2) |  |  |  |
| boundary | a_cs_to_lua_call3_await | n/a | n/a | 316.7 (314.2–356.3) |  |  |  |
| boundary | b_lua_to_cs_call1 | 308.2 (296.8–340.0) | 298.7 (286.0–308.8) | 73.8 (71.9–78.4) | 4.17x | 1.032 | 0.895–1.253 |
| create | c_create_stdlib | n/a | n/a | 43,319 (41,482–44,106) |  |  |  |
| create | c_create_default | 1,193,328 (1,054,872–1,238,515) | 1,254,489 (1,148,600–1,295,107) | n/a |  | 0.951 | 0.707–1.216 |
| create | c_create_complete | 1,277,948 (1,224,016–1,421,334) |  | n/a |  |  |  |
| workload | empty_loop | 56.5 (55.2–58.6) | 57.3 (56.0–65.3) | 5.8 (5.2–7.0) | 9.80x | 0.987 | 0.517–1.191 |
| workload | function_call | 344.5 (336.6–349.1) | 352.3 (332.7–380.0) | 89.3 (82.2–113.8) | 3.86x | 0.978 | 0.620–1.114 |
| workload | method_call | 620.0 (595.9–653.0) | 592.4 (585.6–657.2) | 171.0 (162.0–202.7) | 3.63x | 1.047 | 0.887–1.108 |
| workload | table_array_insert | 1,241 (1,153–1,283) | 1,252 (1,203–1,305) | 87.6 (83.4–91.6) | 14.16x | 0.991 | 0.783–1.150 |
| workload | table_array_read | 393.6 (373.4–481.8) | 384.8 (372.5–432.0) | 47.7 (41.0–52.1) | 8.25x | 1.023 | 0.629–1.644 |
| workload | table_hash_set_get | 738.5 (689.1–781.1) | 688.0 (661.4–689.4) | 99.3 (98.4–102.8) | 7.44x | 1.073 | 1.029–1.350 |
| workload | string_concat | 595.7 (578.1–621.8) | 588.9 (586.5–613.5) | 266.3 (249.7–276.1) | 2.24x | 1.012 | 0.954–1.084 |
| workload | string_format | 4,204 (3,932–4,309) | 4,102 (4,024–4,295) | 319.8 (299.4–333.4) | 13.15x | 1.025 | 0.866–1.134 |
| workload | closure_create_call | 611.0 (590.5–684.0) | 611.0 (599.6–622.8) | 179.7 (177.6–192.3) | 3.40x | 1.000 | 0.960–1.243 |
| workload | int_math | 253.2 (246.0–261.6) | 249.1 (245.9–258.4) | 31.3 (30.8–33.0) | 8.08x | 1.017 | 0.876–1.412 |
| workload | fib_recursive | 180,198 (176,510–190,306) | 178,459 (174,628–180,660) | 41,421 (40,397–43,155) | 4.35x | 1.010 | 0.939–1.616 |
| workload | float_math | 395.9 (386.7–426.5) | 396.1 (393.2–416.2) | 40.8 (40.1–44.7) | 9.71x | 1.000 | 0.832–1.119 |
| workload | string_pattern | 24,028 (23,921–24,958) | 23,712 (22,770–24,823) | 6,474 (6,412–7,279) | 3.71x | 1.013 | 0.741–1.116 |
| workload | table_sort_52 | 127,942 (125,971–141,719) | 133,624 (125,006–141,448) | 30,585 (29,872–33,657) | 4.18x | 0.957 | 0.499–1.396 |
| workload | table_concat | 1,468 (1,419–1,537) | 1,246 (1,215–1,611) | 1,064 (992.3–1,161) | 1.38x | 1.178 | 0.659–1.287 |
| workload | coroutine_switch | 777.0 (743.6–785.5) | 744.0 (715.6–784.7) | 743.2 (730.3–751.9) | 1.05x | 1.044 | 0.724–1.104 |
| workload | gc_small_tables | 1,072 (1,044–1,158) | 1,032 (1,003–1,100) | 342.9 (335.6–369.1) | 3.13x | 1.040 | 0.922–1.488 |
| workload | oop_inherit | 558.7 (530.6–621.9) | 530.3 (523.3–541.8) | 172.4 (161.4–201.3) | 3.24x | 1.054 | 0.954–1.730 |

| Workload | native Lua 5.5.1 (doc) | MoonSharp | Lua-CSharp 0.5.8 | MoonSharp/Lua-CSharp | Lua-CSharp/native | MoonSharp/native |
|---|---:|---:|---:|---:|---:|---:|
| empty_loop | 3.5 | 56.5 (55.2–58.6) | 5.8 (5.2–7.0) | 9.8x | 1.6x | 16.2x |
| function_call | 26.0 | 344.5 (336.6–349.1) | 89.3 (82.2–113.8) | 3.9x | 3.4x | 13.2x |
| method_call | 66.3 | 620.0 (595.9–653.0) | 171.0 (162.0–202.7) | 3.6x | 2.6x | 9.4x |
| table_array_insert | 16.1 | 1,241 (1,153–1,283) | 87.6 (83.4–91.6) | 14.2x | 5.4x | 77.1x |
| table_array_read | 15.7 | 393.6 (373.4–481.8) | 47.7 (41.0–52.1) | 8.2x | 3.0x | 25.1x |
| table_hash_set_get | 35.8 | 738.5 (689.1–781.1) | 99.3 (98.4–102.8) | 7.4x | 2.8x | 20.6x |
| string_concat | 225.8 | 595.7 (578.1–621.8) | 266.3 (249.7–276.1) | 2.2x | 1.2x | 2.6x |
| string_format | 304.8 | 4,204 (3,932–4,309) | 319.8 (299.4–333.4) | 13.1x | 1.0x | 13.8x |
| closure_create_call | 94.0 | 611.0 (590.5–684.0) | 179.7 (177.6–192.3) | 3.4x | 1.9x | 6.5x |
| int_math | 11.3 | 253.2 (246.0–261.6) | 31.3 (30.8–33.0) | 8.1x | 2.8x | 22.4x |
| fib_recursive | 10,085 | 180,198 (176,510–190,306) | 41,421 (40,397–43,155) | 4.4x | 4.1x | 17.9x |
| float_math | 20.8 | 395.9 (386.7–426.5) | 40.8 (40.1–44.7) | 9.7x | 2.0x | 19.0x |
| string_pattern | 3,199 | 24,028 (23,921–24,958) | 6,474 (6,412–7,279) | 3.7x | 2.0x | 7.5x |
| table_sort_52 | 51,009 | 127,942 (125,971–141,719) | 30,585 (29,872–33,657) | 4.2x | 0.6x | 2.5x |
| table_concat | 869.2 | 1,468 (1,419–1,537) | 1,064 (992.3–1,161) | 1.4x | 1.2x | 1.7x |
| coroutine_switch | 118.9 | 777.0 (743.6–785.5) | 743.2 (730.3–751.9) | 1.0x | 6.3x | 6.5x |
| gc_small_tables | 128.8 | 1,072 (1,044–1,158) | 342.9 (335.6–369.1) | 3.1x | 2.7x | 8.3x |
| oop_inherit | 69.6 | 558.7 (530.6–621.9) | 172.4 (161.4–201.3) | 3.2x | 2.5x | 8.0x |

| kind | case | label | alloc B/op thread (median of rounds) | alloc B/op process | rounds |
|---|---|---|---:|---:|---:|
| boundary | a_cs_to_lua_call3 | luacs | 48.00 | 48.00 | 7 |
| boundary | a_cs_to_lua_call3 | msA | 952.00 | 952.00 | 7 |
| boundary | a_cs_to_lua_call3 | msB | 952.00 | 952.00 | 7 |
| boundary | a_cs_to_lua_call3_await | luacs | 48.00 | 48.00 | 7 |
| boundary | a_cs_to_lua_call3_stackapi | luacs | 0.00 | 0.00 | 7 |
| boundary | b_lua_to_cs_call1 | luacs | 0.00 | 0.00 | 7 |
| boundary | b_lua_to_cs_call1 | msA | 504.00 | 504.00 | 7 |
| boundary | b_lua_to_cs_call1 | msB | 504.00 | 504.00 | 7 |
| create | c_create_complete | mscomplete | 193,944.20 | 193,944.20 | 7 |
| create | c_create_default | msA | 185,712.20 | 185,712.20 | 7 |
| create | c_create_default | msB | 185,712.20 | 185,712.20 | 7 |
| create | c_create_stdlib | luacs | 28,720.20 | 28,720.20 | 7 |
| workload | closure_create_call | luacs | 200.00 | 200.00 | 7 |
| workload | closure_create_call | msA | 912.00 | 912.00 | 7 |
| workload | closure_create_call | msB | 912.00 | 912.00 | 7 |
| workload | coroutine_switch | luacs | 0.02 | 0.02 | 7 |
| workload | coroutine_switch | msA | 1,080.01 | 1,080.01 | 7 |
| workload | coroutine_switch | msB | 1,080.01 | 1,080.01 | 7 |
| workload | empty_loop | luacs | 0.00 | 0.00 | 7 |
| workload | empty_loop | msA | 48.00 | 48.00 | 7 |
| workload | empty_loop | msB | 48.00 | 48.00 | 7 |
| workload | fib_recursive | luacs | 0.01 | 0.01 | 7 |
| workload | fib_recursive | msA | 301,368.04 | 301,368.04 | 7 |
| workload | fib_recursive | msB | 301,368.04 | 301,368.04 | 7 |
| workload | float_math | luacs | 0.00 | 0.00 | 7 |
| workload | float_math | msA | 576.00 | 576.00 | 7 |
| workload | float_math | msB | 576.00 | 576.00 | 7 |
| workload | function_call | luacs | 0.00 | 0.00 | 7 |
| workload | function_call | msA | 552.00 | 552.00 | 7 |
| workload | function_call | msB | 552.00 | 552.00 | 7 |
| workload | gc_small_tables | luacs | 704.01 | 704.01 | 7 |
| workload | gc_small_tables | msA | 1,776.04 | 1,776.04 | 7 |
| workload | gc_small_tables | msB | 1,776.04 | 1,776.04 | 7 |
| workload | int_math | luacs | 0.00 | 0.00 | 7 |
| workload | int_math | msA | 288.00 | 288.00 | 7 |
| workload | int_math | msB | 288.00 | 288.00 | 7 |
| workload | method_call | luacs | 0.00 | 0.00 | 7 |
| workload | method_call | msA | 696.00 | 696.00 | 7 |
| workload | method_call | msB | 696.00 | 696.00 | 7 |
| workload | oop_inherit | luacs | 0.00 | 0.00 | 7 |
| workload | oop_inherit | msA | 600.00 | 600.00 | 7 |
| workload | oop_inherit | msB | 600.00 | 600.00 | 7 |
| workload | string_concat | luacs | 84.81 | 84.81 | 7 |
| workload | string_concat | msA | 660.82 | 660.82 | 7 |
| workload | string_concat | msB | 660.82 | 660.82 | 7 |
| workload | string_format | luacs | 174.37 | 174.37 | 7 |
| workload | string_format | msA | 7,999.83 | 7,999.83 | 7 |
| workload | string_format | msB | 7,999.83 | 7,999.83 | 7 |
| workload | string_pattern | luacs | 1,680.00 | 1,680.00 | 7 |
| workload | string_pattern | msA | 49,856.01 | 49,856.01 | 7 |
| workload | string_pattern | msB | 49,856.01 | 49,856.01 | 7 |
| workload | table_array_insert | luacs | 50.33 | 50.33 | 7 |
| workload | table_array_insert | msA | 371.44 | 371.45 | 7 |
| workload | table_array_insert | msB | 371.44 | 371.45 | 7 |
| workload | table_array_read | luacs | 0.00 | 0.00 | 7 |
| workload | table_array_read | msA | 528.00 | 528.00 | 7 |
| workload | table_array_read | msB | 528.00 | 528.00 | 7 |
| workload | table_concat | luacs | 200.00 | 200.00 | 7 |
| workload | table_concat | msA | 1,392.02 | 1,392.02 | 7 |
| workload | table_concat | msB | 1,392.02 | 1,392.02 | 7 |
| workload | table_hash_set_get | luacs | 0.01 | 0.01 | 7 |
| workload | table_hash_set_get | msA | 912.01 | 912.01 | 7 |
| workload | table_hash_set_get | msB | 912.01 | 912.01 | 7 |
| workload | table_sort_52 | luacs | 12,344.06 | 12,344.06 | 7 |
| workload | table_sort_52 | msA | 128,128.41 | 128,128.41 | 7 |
| workload | table_sort_52 | msB | 128,128.41 | 128,128.41 | 7 |

retained bytes per instance (median of rounds, min-max):
  ('c_create_complete', 'mscomplete') 83,620 (83,620–83,620) 7
  ('c_create_default', 'msA') 79,551 (79,551–79,551) 7
  ('c_create_default', 'msB') 79,551 (79,551–79,551) 7
  ('c_create_stdlib', 'luacs') 25,216 (25,216–25,216) 7

distinct checks:
  ('boundary', 'a_cs_to_lua_call3', 'luacs') ['1000002000000']
  ('boundary', 'a_cs_to_lua_call3', 'msA') ['1000002000000']
  ('boundary', 'a_cs_to_lua_call3', 'msB') ['1000002000000']
  ('boundary', 'a_cs_to_lua_call3_await', 'luacs') ['1000002000000']
  ('boundary', 'a_cs_to_lua_call3_stackapi', 'luacs') ['1000002000000']
  ('boundary', 'b_lua_to_cs_call1', 'luacs') ['500001500000']
  ('boundary', 'b_lua_to_cs_call1', 'msA') ['500001500000']
  ('boundary', 'b_lua_to_cs_call1', 'msB') ['500001500000']
  ('create', 'c_create_complete', 'mscomplete') ['ok']
  ('create', 'c_create_default', 'msA') ['ok']
  ('create', 'c_create_default', 'msB') ['ok']
  ('create', 'c_create_stdlib', 'luacs') ['ok']
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
ValueTask completions (sync, async): {'luacs': [112001218, 0], 'msA': [0, 0], 'msB': [0, 0], 'mscomplete': [0, 0]}
