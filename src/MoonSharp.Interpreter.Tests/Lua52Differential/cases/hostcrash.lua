-- Library calls that ended the host process with a CLR exception before the fixes on branch
-- fix/lua52-batch1 (found by a fuzzer that calls every library function with odd arguments).
--@@ io.input missing
local ok, e = pcall(io.input, "this-file-does-not-exist") return ok, e
--@@ io.output bad directory
local ok, e = pcall(io.output, "/this-dir-does-not-exist/x") return ok, e
--@@ io.lines missing
local ok, e = pcall(io.lines, "this-file-does-not-exist") return ok, e
--@@ io.lines exhausted
local n = os.tmpname() local f = io.open(n, "w") f:write("a\nb\n") f:close()
local it = io.lines(n) local s = "" for l in it do s = s .. l end
local ok, e = pcall(it) os.remove(n) return type(it), s, ok, e
--@@ file:lines *a is lazy
local n = os.tmpname() local f = io.open(n, "w") f:write("abc") f:close()
f = io.open(n) local c = 0 for a in f:lines("*a") do c = c + 1 if c == 3 then break end end
f:close() os.remove(n) return c
--@@ seek before start
local n = os.tmpname() local f = io.open(n, "w") f:write("abc") f:close()
f = io.open(n) local a, b, c = f:seek("set", -1) f:close() os.remove(n) return a, b, c
--@@ read negative count
local n = os.tmpname() local f = io.open(n, "w") f:write("abc") f:close()
f = io.open(n) local ok = pcall(f.read, f, -1) f:close() os.remove(n) return type(ok)
--@@ open w+ creates
local n = os.tmpname() os.remove(n) local f = io.open(n, "w+") f:write("x") f:seek("set")
local s = f:read("*a") f:close() os.remove(n) return s
--@@ open a and a+
local n = os.tmpname() local f = io.open(n, "w") f:write("x") f:close()
f = io.open(n, "a") f:write("y") f:close() f = io.open(n, "a+") f:write("z") f:seek("set")
local s = f:read("*a") f:close() os.remove(n) return s
--@@ open r+ missing
local f, e = io.open("this-file-does-not-exist", "r+") return f, e
--@@ open invalid mode
return pcall(io.open, "x", "rw")
--@@ dofile empty name
return pcall(dofile, "")
--@@ loadfile empty name
return loadfile("")
--@@ string.rep 2^31
return (pcall(string.rep, "x", 2^31))
--@@ string.rep huge with separator
return (pcall(string.rep, "ab", 1e10, ","))
--@@ os.time month 13
return os.time({year = 2020, month = 13, day = 1, hour = 0}) == os.time({year = 2021, month = 1, day = 1, hour = 0})
--@@ os.time day 0
return os.time({year = 2020, month = 3, day = 0, hour = 0}) == os.time({year = 2020, month = 2, day = 29, hour = 0})
--@@ os.date 2^53
return os.date("!%Y", 2^53)
--@@ os.date nan
return os.date("!%Y", 0/0)
--@@ format %99.99f of 1e308
return #string.format("%99.99f", 1e308)
--@@ format zero padding to width 0
return string.format("%+00d|%#00x|%#0o", 1, 1, 1)
--@@ format %c of 65536
return string.format("%c", 65536)
