-- metamethods: comparison fallbacks, concat, eq, len, arithmetic, index chains.
--@@ __le falls back to __lt
local mt = {__lt = function(a, b) return a.v < b.v end}
local a, b = setmetatable({v = 1}, mt), setmetatable({v = 2}, mt) return a <= b, b <= a, a >= b
--@@ __le own
local mt = {__le = function(a, b) return "yes" end} local a, b = setmetatable({}, mt), setmetatable({}, mt) return a <= b, a >= b
--@@ __lt result truncated to boolean
local mt = {__lt = function() return "s" end} local a, b = setmetatable({}, mt), setmetatable({}, mt) return a < b, a > b
--@@ __lt different metatables same function
local f = function() return true end local a, b = setmetatable({}, {__lt = f}), setmetatable({}, {__lt = f}) return a < b
--@@ __lt different functions
local a, b = setmetatable({}, {__lt = function() return true end}), setmetatable({}, {__lt = function() return true end}) return pcall(function() return a < b end)
--@@ __lt table and number
local a = setmetatable({}, {__lt = function() return true end}) return pcall(function() return a < 1 end)
--@@ __eq same metatable
local mt = {__eq = function() return true end} return setmetatable({}, mt) == setmetatable({}, mt)
--@@ __eq different metatables same function
local f = function() return true end return setmetatable({}, {__eq = f}) == setmetatable({}, {__eq = f})
--@@ __eq different functions
return setmetatable({}, {__eq = function() return true end}) == setmetatable({}, {__eq = function() return true end})
--@@ __eq only one side
return setmetatable({}, {__eq = function() return true end}) == {}
--@@ __eq table vs number
return setmetatable({}, {__eq = function() return true end}) == 1
--@@ __eq not called for same object
local n = 0 local t = setmetatable({}, {__eq = function() n = n + 1 return false end}) return t == t, n
--@@ __eq result truncated
local mt = {__eq = function() return "x" end} return setmetatable({}, mt) == setmetatable({}, mt), setmetatable({}, mt) ~= setmetatable({}, mt)
--@@ __concat table right
local t = setmetatable({}, {__concat = function(a, b) return "C:" .. type(a) .. type(b) end}) return "s" .. t, 1 .. t, t .. 2
--@@ __concat both tables
local mt = {__concat = function(a, b) return "both" end} return setmetatable({}, mt) .. setmetatable({}, mt)
--@@ __concat chain
local t = setmetatable({}, {__concat = function(a, b) return (type(a) == "table" and "T" or a) .. (type(b) == "table" and "T" or b) end})
return "a" .. "b" .. t .. "c"
--@@ __concat on number via string metatable absent
return pcall(function() return 1 .. nil end)
--@@ __len on table
return #setmetatable({1, 2, 3}, {__len = function(t) return 42 end})
--@@ __len on strings not used
local mt = getmetatable("") mt.__len = function() return 99 end local r = #"abc" mt.__len = nil return r
--@@ __len extra results
return select("#", #setmetatable({}, {__len = function() return 1, 2 end}))
--@@ __unm
return -setmetatable({}, {__unm = function(a, b) return type(b) end})
--@@ __unm second operand
return -setmetatable({}, {__unm = function(a, b) return rawequal(a, b) end})
--@@ __add left number
return 1 + setmetatable({}, {__add = function(a, b) return type(a) .. type(b) end})
--@@ __add string number coercion before meta
return "1" + setmetatable({}, {__add = function(a, b) return type(a) end})
--@@ __div __mod __pow
local mt = {__div = function() return "d" end, __mod = function() return "m" end, __pow = function() return "p" end}
local t = setmetatable({}, mt) return t / 1, t % 1, t ^ 1
--@@ __index function
return setmetatable({}, {__index = function(t, k) return k .. "!" end}).x
--@@ __index chain
local base = {x = 1} local mid = setmetatable({}, {__index = base}) return setmetatable({}, {__index = mid}).x
--@@ __index rawget bypass
return rawget(setmetatable({}, {__index = {x = 1}}), "x")
--@@ __newindex table
local store = {} local t = setmetatable({}, {__newindex = store}) t.a = 1 return rawget(t, "a"), store.a
--@@ __newindex existing key
local n = 0 local t = setmetatable({a = 1}, {__newindex = function() n = n + 1 end}) t.a = 2 return t.a, n
--@@ __call
return setmetatable({}, {__call = function(self, a, b) return a + b end})(2, 3)
--@@ __call result count
return select("#", setmetatable({}, {__call = function() return 1, 2, 3 end})())
--@@ __call nested
local inner = setmetatable({}, {__call = function(self, x) return "inner" .. x end})
local ok, m = pcall(function() return setmetatable({}, {__call = inner})(1) end) return ok
--@@ __tostring
return tostring(setmetatable({}, {__tostring = function() return "obj" end}))
--@@ __metatable protects
local t = setmetatable({}, {__metatable = "locked"}) return getmetatable(t), pcall(setmetatable, t, {})
--@@ __metatable false
local t = setmetatable({}, {__metatable = false}) return getmetatable(t)
--@@ string metatable
return getmetatable("").__index == string
--@@ setmetatable nil removes
local t = setmetatable({}, {}) setmetatable(t, nil) return getmetatable(t)
--@@ setmetatable bad
return (pcall(setmetatable, {}, 1))
--@@ setmetatable non table
return (pcall(setmetatable, 1, {}))
--@@ setmetatable returns table
local t = {} return setmetatable(t, {}) == t
--@@ getmetatable number
return getmetatable(1)
--@@ __gc absent
local called = false
do local t = setmetatable({}, {__gc = function() called = true end}) end
collectgarbage() collectgarbage() return called
--@@ __mode weak keys
local t = setmetatable({}, {__mode = "k"}) t[{}] = 1 collectgarbage() collectgarbage() return next(t) == nil
--@@ __mode weak values
local t = setmetatable({}, {__mode = "v"}) t[1] = {} collectgarbage() collectgarbage() return t[1] == nil
--@@ rawequal
return rawequal({}, {}), rawequal(1, 1.0), rawequal("a", "a"), (pcall(rawequal, 1))
--@@ rawset returns table
local t = {} return rawset(t, "k", 1) == t, t.k
--@@ rawset bad key
return pcall(rawset, {}, nil, 1)
--@@ rawget non table
return (pcall(rawget, "abc", 1))
