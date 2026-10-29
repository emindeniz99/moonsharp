-- error, pcall, xpcall and assert: values, levels and messages.
--@@ error string
error("boom")
--@@ error level 0
error("boom", 0)
--@@ error level 2 has position
local function f() error("boom", 2) end
local ok, m = pcall(function() f() end) return (m:match("^[^:]*:%(?%d") ~= nil), m:match("boom$")
--@@ error level 3
local function f() error("boom", 3) end local function g() f() end
local ok, m = pcall(function() g() end) return (m:match("^[^:]*:%(?%d") ~= nil)
--@@ error level 1 position
local ok, m = pcall(error, "boom") return m
--@@ error number value
local ok, m = pcall(error, 42) return ok, type(m), m
--@@ error number with level
local ok, m = pcall(function() error(42) end) return type(m), m
--@@ error table value
local t = {} local ok, m = pcall(error, t) return ok, m == t
--@@ error table from function
local ok, m = pcall(function() error({code = 7}) end) return type(m), type(m) == "table" and m.code
--@@ error nil
return pcall(error)
--@@ error explicit nil
return pcall(error, nil)
--@@ error boolean
return pcall(error, false)
--@@ error with __tostring object
local ok, m = pcall(error, setmetatable({}, {__tostring = function() return "T" end})) return type(m)
--@@ error rethrow keeps object
local t = {} local ok, m = pcall(function() local ok2, e = pcall(error, t) error(e) end) return m == t
--@@ error string coerced number keeps type
local ok, m = pcall(error, "12") return type(m)
--@@ pcall nil
return pcall(nil)
--@@ pcall no args
return pcall()
--@@ pcall number
return pcall(42)
--@@ pcall result count
return select("#", pcall(function() return 1, 2, 3 end))
--@@ pcall passes args
return pcall(function(...) return select("#", ...), ... end, 1, nil, 3)
--@@ pcall nested
return pcall(pcall, error, "x")
--@@ pcall callable table
return pcall(setmetatable({}, {__call = function(self, a) return a * 2 end}), 21)
--@@ xpcall basic
return xpcall(function() error("e") end, function(m) return "handled: " .. type(m) end)
--@@ xpcall no handler
return xpcall(function() return 1 end)
--@@ xpcall handler nil
return xpcall(function() error("e") end, nil)
--@@ xpcall args
return xpcall(function(a, b) return a + b end, print, 2, 3)
--@@ xpcall handler gets table
local t = {} return xpcall(function() error(t) end, function(m) return m == t end)
--@@ xpcall handler error
return xpcall(function() error("a") end, function(m) error("b") end)
--@@ xpcall handler result count
return select("#", xpcall(function() error("a") end, function(m) return 1, 2 end))
--@@ xpcall traceback handler
local ok, m = xpcall(function() error("a") end, debug.traceback) return ok, type(m), (m:find("stack traceback:", 1, true) ~= nil)
--@@ assert false
return assert(false)
--@@ assert nil message
return assert(nil, "msg")
--@@ assert table message
local t = {} local ok, m = pcall(assert, false, t) return m == t
--@@ assert number message
local ok, m = pcall(assert, false, 42) return type(m), m
--@@ assert returns all
return assert(1, 2, 3)
--@@ assert no args
return assert()
--@@ runtime error call nil global
local ok, m = pcall(function() undefined_fn() end) return m
--@@ runtime error call nil field
local t = {} local ok, m = pcall(function() t.x() end) return m
--@@ runtime error call nil method
local t = {} local ok, m = pcall(function() t:x() end) return m
--@@ runtime error index nil local
local ok, m = pcall(function() local a a.b = 1 end) return m
--@@ runtime error arith nil upvalue
local u local ok, m = pcall(function() return u + 1 end) return m
--@@ runtime error compare
local ok, m = pcall(function() return {} < {} end) return m
--@@ runtime error compare mixed
local ok, m = pcall(function() return 1 < "x" end) return m
--@@ runtime error concat
local ok, m = pcall(function() return "a" .. {} end) return m
--@@ runtime error concat nil
local ok, m = pcall(function() local x return "a" .. x end) return m
--@@ runtime error length
local ok, m = pcall(function() return #5 end) return m
--@@ bad argument message
return pcall(string.rep)
--@@ bad argument method self
return pcall(function() local s = 5 return ("x").rep(nil, 2) end)
--@@ bad argument method call
local ok, m = pcall(function() return ("x"):rep({}) end) return m
--@@ stack overflow message
local function r() return 1 + r() end local ok, m = pcall(r) return ok, (m:find("stack overflow", 1, true) ~= nil)
--@@ error in __index
local t = setmetatable({}, {__index = function() error("idx") end}) local ok, m = pcall(function() return t.x end) return m
--@@ error in __tostring via tostring
return pcall(tostring, setmetatable({}, {__tostring = function() error("ts") end}))
--@@ chunkname in message
local f = load("error('x')", "=mychunk") local ok, m = pcall(f) return m
--@@ default load chunkname in message
local f = load("error('x')") local ok, m = pcall(f) return m
--@@ syntax error message from load
return load("x = = 1", "=src")
--@@ syntax error default chunkname
return load("x = = 1")
