-- Base library odds and ends, collectgarbage, os and string helpers.
--@@ collectgarbage count type
return type(collectgarbage("count"))
--@@ collectgarbage count results
return select("#", collectgarbage("count"))
--@@ collectgarbage collect result
return collectgarbage("collect"), collectgarbage()
--@@ collectgarbage step
return type(collectgarbage("step"))
--@@ collectgarbage isrunning
return collectgarbage("isrunning")
--@@ collectgarbage setpause
return type(collectgarbage("setpause", 100))
--@@ collectgarbage invalid
return (pcall(collectgarbage, "nooption"))
--@@ collectgarbage invalid message
return collectgarbage("nooption")
--@@ collectgarbage option case
return collectgarbage("COUNT")
--@@ collectgarbage number option
return collectgarbage(1)
--@@ collectgarbage table option
return collectgarbage({})
--@@ collectgarbage boolean option
return collectgarbage(true)
--@@ collectgarbage nil option
return collectgarbage(nil)
--@@ collectgarbage bad second argument
return collectgarbage("count", "x")
--@@ collectgarbage bad second argument with default option
return collectgarbage(nil, {})
--@@ collectgarbage option checked before second argument
return collectgarbage("x", {})
--@@ collectgarbage numeric string second argument
return select("#", collectgarbage("count", "10")), collectgarbage("collect", 2.7)
--@@ collectgarbage count remainder
local kb, b = collectgarbage("count") return kb > 0, b == math.floor(b), b >= 0 and b < 1024
--@@ collectgarbage count is kb
local kb, b = collectgarbage("count") return (kb * 1024 - b) % 1024
--@@ collectgarbage stop and restart
return collectgarbage("stop"), collectgarbage("restart"), collectgarbage("isrunning")
--@@ collectgarbage generational and incremental
return collectgarbage("generational"), collectgarbage("incremental")
--@@ collectgarbage tuning results
return type(collectgarbage("setstepmul", 200)), type(collectgarbage("setmajorinc", 200)), type(collectgarbage("setpause", 200))
--@@ collectgarbage step loop ends
local n = 0 repeat n = n + 1 until collectgarbage("step") or n > 100000 return n <= 100000
--@@ collectgarbage results count
return select("#", collectgarbage()), select("#", collectgarbage("step")), select("#", collectgarbage("isrunning")), select("#", collectgarbage("setpause", 200))
--@@ type no arg
return (pcall(type))
--@@ type values
return type(nil), type(1), type(""), type({}), type(print), type(type), type(coroutine.create(print))
--@@ print returns nothing
return select("#", print())
--@@ select and varargs in main chunk
return select("#", ...)
--@@ rawlen non table
return (pcall(rawlen, 5))
--@@ next is pairs iterator
return pairs({}) == next
--@@ os.time table
return os.time({year = 2020, month = 1, day = 1, hour = 12}) - os.time({year = 2020, month = 1, day = 1, hour = 11})
--@@ os.time default hour
local a = os.time({year = 2020, month = 6, day = 15}) local b = os.time({year = 2020, month = 6, day = 15, hour = 12}) return a == b
--@@ os.time missing field
return pcall(os.time, {year = 2020})
--@@ os.date fields
local t = os.date("*t", 0) return type(t.year), type(t.isdst), t.year >= 1969
--@@ os.date utc format
return os.date("!%Y-%m-%d %H:%M:%S", 86400 + 3661)
--@@ os.date formats
return os.date("!%a %A %b %B %d %j %p %y %%", 0)
--@@ os.date invalid conversion
return pcall(os.date, "%Ez", 0)
--@@ os.difftime
return os.difftime(10, 4), os.difftime(10)
--@@ os.clock type
return type(os.clock())
--@@ os.getenv missing
return os.getenv("THIS_ENV_VAR_DOES_NOT_EXIST_12345")
--@@ os.setlocale unknown
return os.setlocale("xx_YY.nonexistent")
--@@ os.setlocale query
return os.setlocale()
--@@ string.len non string
return pcall(string.len, 12), pcall(string.len, nil)
--@@ string.format integer float arg
return string.format("%d", 3.0), pcall(string.format, "%d", 3.5)
--@@ string.find plain special
return string.find("a.b", ".", 1, true), string.find("a+b", "+", 1, true)
--@@ string.find init negative
return string.find("abcabc", "b", -2), string.find("abc", "b", -10)
--@@ string.gsub repl count
return string.gsub("aaaa", "a", "b", 2)
--@@ string.gsub repl percent
return string.gsub("abc", "b", "%%"), pcall(string.gsub, "abc", "b", "%2")
--@@ string.gsub table __index
return string.gsub("x y", "%w", setmetatable({}, {__index = function(t, k) return k:upper() end}))
--@@ string.gmatch anchor
local n = 0 for w in string.gmatch("aaa", "^a") do n = n + 1 end return n
--@@ tostring integer float
return tostring(3) == tostring(3.0), 3 == 3.0, math.type
--@@ number keys 2^53
local t = {} t[2^53] = 1 return t[2^53 + 1]
--@@ string comparison
return "a" < "b", "Z" < "a", "" < "a", "a\0b" < "a\0c", "10" < "9"
--@@ concat number formatting
return 1 .. 2, 1.5 .. "", -0.0 .. "", 2^63 .. ""
