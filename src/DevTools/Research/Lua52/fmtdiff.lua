-- Prints one line per string.format case; run on lua5.2 and on the fork and diff.
local function str(s)
  local out = {}
  for i = 1, #s do
    local b = string.byte(s, i)
    if b >= 32 and b < 127 and b ~= 92 then out[#out + 1] = string.char(b) else out[#out + 1] = "\\" .. b end
  end
  return table.concat(out)
end
local vals = {0, -0.0, 1, -1, 7, 42, 0.5, -0.5, 0.1, 2.5, -2.5, 3.14159265358979, 1/3, 2/3, 99.999, 1.005, 0.125, 0.375,
  123456.789, 1e-5, 1e-300, 5e-324, 2.2250738585072014e-308, 1e15, 1e16, 1e20, -1e20, 1e21, 1e22, 1e23, 1e100, 1e308, -1e308,
  1.7976931348623157e308, 2^31, 2^32, -2^32, 2^53 - 1, 2^53, 2^63, -2^63, 2^64, 255, 65, 9.5, 0.05, 0.15, 0.25, 0.35, 1234.5,
  1234.25, 999999.5, 9999995, 0.000123456, 6.02e23, 1.5e300, 1/0, -1/0, 0.0000001, 123456789012, 0.9999999999,
  "10", "0x10", " 12 ", "1e2"}
local flags = {"", "-", "+", " ", "#", "0", "-0", "+0", " 0", "#0", "+ ", "-#", "-+ #0"}
local widths = {"", "1", "8", "20", "99"}
local precs = {"", ".", ".0", ".1", ".2", ".3", ".6", ".10", ".17", ".20", ".40", ".99"}
local convs = {"d", "i", "u", "c", "x", "X", "o", "e", "E", "f", "g", "G", "a", "A"}
local only = arg and arg[1]
for _, c in ipairs(convs) do
  if not only or only == c then
    for _, f in ipairs(flags) do for _, w in ipairs(widths) do for _, p in ipairs(precs) do
      local fmt = "%" .. f .. w .. p .. c
      for _, v in ipairs(vals) do
        local ok, r = pcall(string.format, "[" .. fmt .. "]", v)
        if ok then r = str(r) else r = "ERR " .. tostring(r):gsub("^.-:%d+: ", "") end
        io.write(fmt, " ", type(v) == "string" and ("'" .. v .. "'") or string.format("%.17g", v), "\t", r, "\n")
      end
    end end end
  end
end
