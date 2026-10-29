-- coroutine library.
--@@ status lifecycle
local co co = coroutine.create(function() coroutine.yield(coroutine.status(co)) end)
local s0 = coroutine.status(co) local _, s1 = coroutine.resume(co) local s2 = coroutine.status(co)
coroutine.resume(co) return s0, s1, s2, coroutine.status(co)
--@@ status normal
local a, b
a = coroutine.create(function() coroutine.resume(b) end)
local seen b = coroutine.create(function() seen = coroutine.status(a) end)
coroutine.resume(a) return seen
--@@ running main
local co, ismain = coroutine.running() return type(co), ismain
--@@ running inside
local co = coroutine.create(function() local c, m = coroutine.running() return type(c), m end)
return coroutine.resume(co)
--@@ resume dead
local co = coroutine.create(function() end) coroutine.resume(co) return coroutine.resume(co)
--@@ resume non coroutine
return coroutine.resume(42)
--@@ resume self
local co co = coroutine.create(function() return coroutine.resume(co) end) return coroutine.resume(co)
--@@ resume passes values
local co = coroutine.create(function(a, b) local c, d = coroutine.yield(a + b) return c * d end)
local r1 = {coroutine.resume(co, 1, 2)} local r2 = {coroutine.resume(co, 3, 4)} return r1[1], r1[2], r2[1], r2[2]
--@@ resume result count
local co = coroutine.create(function() coroutine.yield() end) return select("#", coroutine.resume(co))
--@@ resume error value
local co = coroutine.create(function() error({}) end) local ok, e = coroutine.resume(co) return ok, type(e)
--@@ resume error string
local co = coroutine.create(function() error("x") end) local ok, e = coroutine.resume(co) return ok, e
--@@ wrap multiple values
local f = coroutine.wrap(function() coroutine.yield(1, 2, 3) end) return f()
--@@ wrap error propagates
local f = coroutine.wrap(function() error("inner") end) local ok, m = pcall(f) return ok, m
--@@ wrap error table
local t = {} local f = coroutine.wrap(function() error(t) end) local ok, m = pcall(f) return m == t
--@@ wrap dead
local f = coroutine.wrap(function() end) f() local ok, m = pcall(f) return ok, m
--@@ wrap type
return type(coroutine.wrap(function() end))
--@@ yield outside
return pcall(coroutine.yield, 1)
--@@ yield across pcall
local co = coroutine.create(function() return pcall(function() coroutine.yield("in") return "out" end) end)
local a = {coroutine.resume(co)} local b = {coroutine.resume(co)} return a[2], b[2], b[3]
--@@ yield across metamethod
local t = setmetatable({}, {__index = function(t, k) return coroutine.yield(k) end})
local co = coroutine.create(function() return t.foo end)
local _, k = coroutine.resume(co) local _, v = coroutine.resume(co, "bar") return k, v
--@@ yield across table.sort
local co = coroutine.create(function() table.sort({3, 2, 1}, function(a, b) coroutine.yield() return a < b end) end)
return coroutine.resume(co)
--@@ yield in iterator
local function gen() return coroutine.wrap(function() for i = 1, 3 do coroutine.yield(i) end end) end
local s = 0 for v in gen() do s = s + v end return s
--@@ isyieldable absent
return coroutine.isyieldable
--@@ create non function
return pcall(coroutine.create, 1)
--@@ create C function
local co = coroutine.create(print) return type(co), coroutine.status(co)
--@@ status bad arg
return pcall(coroutine.status, {})
