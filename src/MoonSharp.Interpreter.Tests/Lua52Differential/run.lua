-- Differential runner: runs every case of a case file and prints one line per
-- case, so the output of reference Lua 5.2 and of MoonSharp can be diffed.
--   lua5.2 run.lua cases/math.lua > expected/math.txt
-- A case starts with a line "--@@ <name>" and runs as its own chunk named "=c"
-- with a fresh environment that falls back to _G. Results are written without
-- tostring or string.format, so number formatting differences do not hide or
-- fake a difference: integers in decimal, other numbers as mantissa*2^exponent.
-- An optional second argument starts at that case number, so a runner can go
-- on after a case that ends the process (an exception pcall cannot catch).

local function num(v)
  if v ~= v then return "nan" end
  if v == 1/0 then return "inf" end
  if v == -1/0 then return "-inf" end
  if v == 0 then return (1/v < 0) and "-0" or "0" end
  if v == math.floor(v) and v > -2^53 and v < 2^53 then
    local neg = v < 0
    if neg then v = -v end
    local digits = {}
    while v > 0 do
      local d = v % 10
      digits[#digits + 1] = string.char(48 + d)
      v = (v - d) / 10
    end
    return (neg and "-" or "") .. string.reverse(table.concat(digits))
  end
  local m, e = math.frexp(v)
  return num(m * 2^53) .. "p" .. num(e - 53)
end

local function str(s)
  local out = {}
  for i = 1, #s do
    local b = string.byte(s, i)
    if b >= 32 and b < 127 and b ~= 92 and b ~= 34 then
      out[#out + 1] = string.char(b)
    else
      out[#out + 1] = "\\" .. num(b)
    end
  end
  return '"' .. table.concat(out) .. '"'
end

local function ser(v)
  local t = type(v)
  if t == "number" then return num(v) end
  if t == "string" then return str(v) end
  if t == "nil" or t == "boolean" then return t == "nil" and "nil" or (v and "true" or "false") end
  return t
end

-- Strips the position from an error message: "c:12: msg" (Lua) and
-- "=c:(12,3-8): msg" (MoonSharp keeps the "=") both become "msg".
local function msg(m)
  if type(m) ~= "string" then return "<" .. ser(m) .. ">" end
  m = string.gsub(m, "^=?c:%d+: ", "")
  m = string.gsub(m, "^=?c:%([%d,%-]+%): ", "")
  return str(m)
end

local f = assert(io.open(arg[1], "r"))
local text = f:read("*a")
f:close()

local cases, name, buf = {}, nil, {}
for line in string.gmatch(text .. "\n", "([^\n]*)\n") do
  local n = string.match(line, "^%-%-@@ (.+)$")
  if n then
    if name then cases[#cases + 1] = { name, table.concat(buf, "\n") } end
    name, buf = n, {}
  elseif name then
    buf[#buf + 1] = line
  end
end
if name then cases[#cases + 1] = { name, table.concat(buf, "\n") } end

for index = tonumber(arg[2]) or 1, #cases do
  local c = cases[index]
  local env = setmetatable({}, { __index = _G })
  local fn, err = load(c[2], "=c", "t", env)
  local line
  if not fn then
    line = "SYNTAX\t" .. msg(err)
  else
    local r = table.pack(pcall(fn))
    if r[1] then
      local vals = {}
      for i = 2, r.n do vals[#vals + 1] = ser(r[i]) end
      line = "OK\t" .. (r.n - 1) .. "\t" .. table.concat(vals, " ")
    else
      line = "ERR\t" .. msg(r[2])
    end
  end
  io.write(c[1], "\t", line, "\n")
end
