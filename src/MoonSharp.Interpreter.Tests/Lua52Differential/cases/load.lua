-- load, loadstring, dofile and environments.
--@@ load string
return load("return 1 + 1")()
--@@ load env
return load("return x", "=e", "t", {x = 5})()
--@@ load env number
return load("return _ENV", "=e", "t", 5)()
--@@ load env nil explicit
local f = load("return x", "=e", "t", nil) return pcall(f)
--@@ load mode b on text
return load("return 1", "=e", "b")
--@@ load mode x
return load("return 1", "=e", "x")
--@@ load mode t on binary
return load(string.dump(function() return 1 end), "=e", "t")
--@@ load binary
return load(string.dump(function() return 7 end))()
--@@ load reader pieces
local parts = {"return ", "4", "2"} local i = 0 return load(function() i = i + 1 return parts[i] end)()
--@@ load reader empty string ends
local parts = {"return 5", ""} local i = 0 return load(function() i = i + 1 return parts[i] end)()
--@@ load reader error
return load(function() error("rd") end)
--@@ load reader bad type
return load(function() return {} end)
--@@ load reader nil first
local f = load(function() return nil end) return type(f), f and select("#", f())
--@@ load syntax error result count
return select("#", load("x ="))
--@@ load non string
return pcall(load, 42)
--@@ load chunk varargs
return load("return ...")(1, 2)
--@@ loadstring present
return type(loadstring)
--@@ loadstring works
return loadstring and loadstring("return 3")()
--@@ loadstring chunkname
return select(2, loadstring("x x", "=name"))
--@@ loadstring syntax error result count
return select("#", loadstring("x ="))
--@@ loadstring varargs
return loadstring("return ...")(1, 2)
--@@ loadstring reader
local parts = {"return ", "4", "2"} local i = 0 return loadstring(function() i = i + 1 return parts[i] end)()
--@@ loadstring mode
return loadstring("return 1", "=e", "b")
--@@ loadstring env
return loadstring("return x", "=e", "t", {x = 5})()
--@@ loadstring no argument
return loadstring()
--@@ loadstring table
return loadstring({})
--@@ loadstring number
return loadstring(42)
--@@ loadstring is load
return loadstring == load
--@@ setfenv absent
return setfenv, getfenv
--@@ module absent
return type(module)
--@@ _ENV local
local function f() local _ENV = {y = 9} return y end return f()
--@@ _ENV upvalue global write
local env = {} local f = load("z = 1", "=e", "t", env) f() return env.z, rawget(_G, "z")
--@@ _G identity
return _G == _ENV, _G._G == _G
--@@ _VERSION
return _VERSION
--@@ string.dump of C function
return pcall(string.dump, print)
--@@ string.dump with upvalues
local u = 1 return pcall(string.dump, function() return u end)
--@@ dofile missing
return pcall(dofile, "this-file-does-not-exist.lua")
--@@ loadfile missing
return loadfile("this-file-does-not-exist.lua")
--@@ require missing
local ok, m = pcall(require, "no_such_module_xyz") return ok, type(m)
--@@ package fields
return type(package.path), type(package.cpath), type(package.loaded), type(package.preload), type(package.searchers), type(package.config), type(package.searchpath)
--@@ package.loaded entries
return package.loaded._G == _G, package.loaded.string == string, package.loaded.package == package
