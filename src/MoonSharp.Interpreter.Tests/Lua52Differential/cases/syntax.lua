-- Parser and compiler: operator precedence, multiple results, assignment order, goto.
--@@ unary minus power right operand
local x = 3 return 1 + -x^2, 2 * -x^2, 1 - -x^2
--@@ unary minus power after and
return true and -2^2
--@@ unary minus power in comparison
local x = 2 return 0 > -x^2, -x^2 == -4
--@@ not power
return not 2^2, true and not 2^2
--@@ unary minus power primary
local x = 3 return -x^2, - - -x^2, -2^-2
--@@ length power
local t = {1, 2} return #t^2, 1 + #t^2
--@@ and truncates call results
local function f() return 1, 2, 3 end return 3 and f()
--@@ or truncates call results
local function f() return 1, 2, 3 end return nil or f()
--@@ and/or truncate in assignment
local function f() return 1, 2 end local a, b = true and f() return a, b
--@@ and/or truncate in table constructor
local function f() return 1, 2 end return #{false or f()}
--@@ and/or truncate as argument
local function f() return 1, 2 end return select("#", nil or f())
--@@ parenthesis truncates
local function f() return 1, 2 end return (f())
--@@ comparison truncates
local function f() return 1, 2 end return f() == 1
--@@ multiple assignment evaluates prefixes first
local a, i = {}, 1 i, a[i] = i + 1, 20 return i, a[1], a[2]
--@@ multiple assignment swap
local a = {1, 2} a[1], a[2] = a[2], a[1] return a[1], a[2]
--@@ multiple assignment table replaced
local t = {} local u = t t, t.x = {}, 1 return u.x, t.x
--@@ multiple assignment upvalue order
local a, i = {}, 1 local function f() i, a[i] = i + 1, 20 end f() return i, a[1], a[2]
--@@ multiple assignment extra values
local a, b = 1, 2, 3 return a, b
--@@ multiple assignment call expands last
local function f() return 1, 2 end local a, b, c = f(), f() return a, b, c
--@@ goto continue
local s = 0 for i = 1, 5 do if i % 2 == 0 then goto continue end s = s + i ::continue:: end return s
--@@ goto end of block after local
do goto e local x = 1 ::e:: end return "ok"
--@@ goto end of block after local with semicolons
do goto e local x = 1 ::e:: ; ; end return "ok"
--@@ goto into local scope rejected
return load("do goto e local x = 1 ::e:: x = 2 end")
--@@ goto out of nested for
local n = 0 for i = 1, 3 do for j = 1, 3 do if j == 2 then goto next end n = n + 1 end ::next:: end return n
--@@ goto backwards loop
local i = 0 ::top:: i = i + 1 if i < 5 then goto top end return i
--@@ goto out of for keeps outer loop
local s = 0 for i = 1, 3 do for k, v in pairs({1}) do goto out end ::out:: s = s + i end return s
--@@ duplicate label
return load("::a:: ::a::")
--@@ label visible in nested function rejected
return load("::a:: local function f() goto a end")
--@@ break outside loop
return load("break")
--@@ return not last
return load("return 1 print(1)")
--@@ return with semicolons
return load("return;;")
--@@ vararg outside vararg function
return load("function f() return ... end")
--@@ two varargs
return load("function f(..., ...) end")
--@@ long string levels
return [==[a]]b]=]c]==]
--@@ long string leading newline
return [[
x]]
--@@ long string CRLF
return load("return [[\r\nx\r\ny]]")()
--@@ escapes
return "\65\066\x41\z
        B\
C"
--@@ escape decimal too large
return load([[return "\256"]])
--@@ escape unknown
return load([[return "\q"]])
--@@ unicode escape
return load([[return "\u{48}"]])
--@@ number literal forms
return 0x10, 0xA.8p0, 1e2, .5, 3., 0x.1
--@@ number followed by letters
return load("return 3x")
--@@ number exponent then plus
return load("return 1e15+0.5")
--@@ identifier with high byte
return load("\200x = 1")
--@@ integer division operator absent
return load("return 7 // 2")
--@@ bitwise operators absent
return load("return 1 & 2")
--@@ comment at end without newline
return load("return 1 -- comment")()
--@@ shebang in load
return load("#!/bin/lua\nreturn 1")
--@@ method call on string literal needs parens
return load("return 'x':rep(2)")
--@@ ambiguous call across newline
return load("local a = f\n(g)()")
--@@ deep nesting limit
return load("return " .. string.rep("(", 300) .. "1" .. string.rep(")", 300)) ~= nil
--@@ many locals limit
local t = {} for i = 1, 210 do t[i] = "a" .. i end return load("local " .. table.concat(t, ",")) ~= nil
--@@ many upvalues limit
local t, u = {}, {} for i = 1, 260 do t[i] = "local a" .. i .. " = " .. i u[i] = "a" .. i end
return load(table.concat(t, "\n", 1, 180) .. "\n do " .. table.concat(t, "\n", 181, 260) .. "\n return function() return " .. table.concat(u, "+") .. " end end") ~= nil
--@@ numeric for loop variable is a copy
local t = {} for i = 1, 3 do t[i] = function() return i end i = i * 10 end return t[1](), t[3]()
--@@ for loop does not see outer reassign
local n = 3 local c = 0 for i = 1, n do n = 10 c = c + 1 end return c
--@@ while with break value
local i = 0 while true do i = i + 1 if i > 3 then break end end return i
--@@ repeat scope sees local in until
local i = 0 repeat local j = i i = i + 1 until j >= 2 return i
--@@ semicolon statements
;;; local a = 1; ; return a
--@@ string call syntax
local function f(s) return s .. "!" end return f"x", f[[y]], type(f{})
--@@ method definition self
local o = {v = 1} function o:get() return self.v end return o:get(), o.get(o)
--@@ nested function name
local a = {b = {}} function a.b.c() return 1 end return a.b.c()
--@@ local function recursion
local function f(n) if n == 0 then return 0 end return n + f(n - 1) end return f(10)
--@@ local function not recursive with local f =
local f = function(n) return n end local f = function(n) if n == 0 then return "inner" end return f(0) end return f(1)
