-- table library with odd arguments, and the length operator on tables with holes.
--@@ concat numbers
return table.concat({1, 2.5, 1e15, 2^53, -0.0, 1/3}, ",")
--@@ concat sep number
return table.concat({"a", "b"}, 1)
--@@ concat float indices
return table.concat({"a", "b", "c"}, ",", 1.5, 2.9)
--@@ concat string indices
return table.concat({"a", "b", "c"}, ",", "2", "3")
--@@ concat i > j
return table.concat({"a"}, ",", 3, 2)
--@@ concat bad value
return table.concat({"a", {}, "c"})
--@@ concat bad value message position
return table.concat({"a", "b", true}, ",", 1, 3)
--@@ concat nil hole
return table.concat({"a", nil, "c"}, ",", 1, 3)
--@@ concat negative range
local t = {[-1] = "x", [0] = "y", "z"} return table.concat(t, ",", -1, 1)
--@@ concat __index ignored
return table.concat(setmetatable({}, {__index = function(t, i) return "v" .. i end}), ",", 1, 2)
--@@ concat no table
return table.concat("abc")
--@@ insert append
local t = {1, 2} table.insert(t, 3) return t[1], t[2], t[3], #t
--@@ insert pos
local t = {1, 2} table.insert(t, 1, 0) return t[1], t[2], t[3]
--@@ insert pos n+1
local t = {1, 2} table.insert(t, 3, 9) return t[3]
--@@ insert pos n+2
local t = {1, 2} table.insert(t, 4, 9) return t[4]
--@@ insert pos 0
local t = {1, 2} table.insert(t, 0, 9) return t[0]
--@@ insert pos float
local t = {1, 2} table.insert(t, 1.5, 9) return t[1], t[2], t[3]
--@@ insert pos string
local t = {1, 2} table.insert(t, "1", 9) return t[1], t[2], t[3]
--@@ insert nil value
local t = {1, 2} table.insert(t, nil) return #t, t[3]
--@@ insert pos nil value
local t = {1, 2} table.insert(t, 1, nil) return #t, t[1], t[2], t[3]
--@@ insert too many
local t = {} table.insert(t, 1, 2, 3) return #t
--@@ insert too few
local t = {} table.insert(t) return #t
--@@ insert returns nothing
return select("#", table.insert({}, 1))
--@@ insert __len
local t = setmetatable({}, {__len = function() return 5 end}) table.insert(t, "x") return rawget(t, 6), rawget(t, 1)
--@@ insert __newindex ignored
local log = {} local t = setmetatable({}, {__newindex = function(t, k, v) log[#log + 1] = k end}) table.insert(t, "x") return #log, rawget(t, 1)
--@@ remove last
local t = {1, 2, 3} return table.remove(t), #t
--@@ remove first
local t = {1, 2, 3} return table.remove(t, 1), t[1], t[2], t[3]
--@@ remove empty
local t = {} return table.remove(t)
--@@ remove empty pos 1
local t = {} return table.remove(t, 1)
--@@ remove empty pos 0
local t = {} return table.remove(t, 0)
--@@ remove n+1
local t = {1, 2} return table.remove(t, 3)
--@@ remove n+2
local t = {1, 2} return table.remove(t, 4)
--@@ remove pos 0 nonempty
local t = {1, 2} return table.remove(t, 0)
--@@ remove negative
local t = {1, 2} return table.remove(t, -1)
--@@ remove float pos
local t = {1, 2, 3} return table.remove(t, 1.5), t[1], t[2]
--@@ remove extra arg
local t = {1, 2} return table.remove(t, 1, "extra"), #t
--@@ remove result count empty
return select("#", table.remove({}))
--@@ remove queue idiom
local q = {"a"} local x = table.remove(q, 1) local y = table.remove(q, 1) return x, y, #q
--@@ unpack basic
return table.unpack({1, 2, 3})
--@@ unpack range
return table.unpack({1, 2, 3}, 2, 3)
--@@ unpack empty range
return select("#", table.unpack({1, 2, 3}, 3, 2))
--@@ unpack i > j + 1
return select("#", table.unpack({}, 2))
--@@ unpack i much larger
return select("#", table.unpack({1}, 5, 1))
--@@ unpack negative
return table.unpack({[-1] = "a", [0] = "b", "c"}, -1, 1)
--@@ unpack floats
return table.unpack({1, 2, 3}, 1.5, 2.5)
--@@ unpack nil holes
return select("#", table.unpack({1, nil, 3}))
--@@ unpack too many
return pcall(function() return select("#", table.unpack({}, 1, 1e7)) end)
--@@ unpack huge
return table.unpack({}, 1, 2^40)
--@@ unpack global alias
return unpack == table.unpack, unpack({7})
--@@ unpack __index ignored
return table.unpack(setmetatable({}, {__index = function() return 1 end}), 1, 2)
--@@ pack
local t = table.pack(1, nil, 3) return t.n, t[1], t[2], t[3], #t
--@@ pack none
local t = table.pack() return t.n, next(t)
--@@ sort numbers
local t = {5, 2, 8, 1, 9, 3} table.sort(t) return table.concat(t, ",")
--@@ sort strings
local t = {"b", "A", "a", "B", "10", "9"} table.sort(t) return table.concat(t, ",")
--@@ sort desc comparator
local t = {5, 2, 8, 1} table.sort(t, function(a, b) return a > b end) return table.concat(t, ",")
--@@ sort mixed types
local t = {1, "2", 3} table.sort(t) return t[1]
--@@ sort with nil hole
local t = {3, nil, 1} table.sort(t) return t[1], t[2], t[3]
--@@ sort __lt
local mt = {__lt = function(a, b) return a.v < b.v end}
local t = {} for i, v in ipairs({4, 1, 3}) do t[i] = setmetatable({v = v}, mt) end
table.sort(t) return t[1].v, t[2].v, t[3].v
--@@ sort comparator non-boolean
local t = {3, 1, 2} table.sort(t, function(a, b) if a < b then return 1 end end) return table.concat(t, ",")
--@@ sort comparator error
local t = {3, 1, 2} table.sort(t, function(a, b) error("cmp") end)
--@@ sort bad comparator type
table.sort({1, 2}, 3)
--@@ sort one element
local t = {1} table.sort(t, function() error("not called") end) return t[1]
--@@ sort calls per element
local n = 0 local t = {} for i = 1, 100 do t[i] = (i * 37) % 101 end
table.sort(t, function(a, b) n = n + 1 return a < b end) return n < 1000, t[1], t[100]
--@@ sort __len
local t = setmetatable({3, 2, 1, 0}, {__len = function() return 3 end}) table.sort(t) return t[1], t[2], t[3], t[4]
--@@ sort returns nothing
return select("#", table.sort({2, 1}))
--@@ maxn
return table.maxn and table.maxn({1, 2, [10] = 3, [2.5] = 4})
--@@ length holes constructor
return #{nil, 2, 3}, #{1, nil, 3}, #{1, 2, nil}, #{nil, nil, 3}, #{n = 1, nil, 2}
--@@ length holes assignment
local t = {} t[1] = 1 t[2] = 2 t[4] = 4 return #t
--@@ length after nil end
local t = {1, 2, 3} t[3] = nil return #t
--@@ length after nil middle
local t = {1, 2, 3, 4} t[2] = nil return #t
--@@ length varargs with nil
local function f(...) local t = {...} return #t, select("#", ...) end return f(1, nil, 3)
--@@ length __len table
return #setmetatable({}, {__len = function() return 7 end})
--@@ length __len returns string
return #setmetatable({}, {__len = function() return "x" end})
--@@ rawlen
return rawlen({1, 2}), rawlen("abc"), (pcall(rawlen, 5))
--@@ next result count end
return select("#", next({}))
--@@ next result count
return select("#", next({10}))
--@@ next after last
return select("#", next({10}, 1))
--@@ next invalid key
return next({1}, "nope")
--@@ pairs result count
return select("#", pairs({}))
--@@ ipairs result count
return select("#", ipairs({}))
--@@ ipairs stops at nil
local n = 0 for i, v in ipairs({1, 2, nil, 4}) do n = i end return n
--@@ ipairs __index
local t = setmetatable({}, {__index = function(t, i) if i <= 3 then return i * 10 end end})
local s = 0 for i, v in ipairs(t) do s = s + v end return s
--@@ ipairs first values
local f, s, i = ipairs({"a"}) return type(f), type(s), i, f(s, i)
--@@ ipairs step result count
local f, s, i = ipairs({"a"}) return select("#", f(s, 1))
--@@ pairs __pairs result count
local t = setmetatable({}, {__pairs = function() return 1, 2, 3, 4, 5 end}) return select("#", pairs(t))
--@@ pairs __pairs
local t = setmetatable({}, {__pairs = function(t) return function(_, k) if not k then return 1, "one" end end, t, nil end})
local out = {} for k, v in pairs(t) do out[#out + 1] = k .. v end return table.concat(out)
--@@ ipairs __ipairs
local t = setmetatable({}, {__ipairs = function(t) return function(_, i) if i < 2 then return i + 1, "x" end end, t, 0 end})
local n = 0 for i in ipairs(t) do n = n + 1 end return n
--@@ pairs non-table
return pairs(5)
--@@ ipairs non-table
return ipairs(nil)
--@@ modify during traversal
local t = {a = 1, b = 2, c = 3} for k in pairs(t) do t[k] = nil end return next(t)
--@@ float keys normalised
local t = {} t[1.0] = "a" t[2] = "b" return t[1], #t, t[2.0]
--@@ nan key
local t = {} return pcall(function() t[0/0] = 1 end)
--@@ nil key
local t = {} t[nil] = 1
--@@ nil key read
local t = {} return t[nil]
