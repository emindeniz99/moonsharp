local vals = {0, 1, -1, 2, -2, 0.5, 1.5, 2.5, -1.5, -2.5, 3.7, -3.7, 31, 32, 33, -31, -32, -33, 64, 2^31, 2^32, 2^32 + 1, -2^31, -2^32, 2^40 + 7, -2^40 - 7,
  0x7fffffff, 0x80000000, 0xffffffff, 0x12345678, 0xdeadbeef, 1e10, -1e10, 2^50 + 0.5, -(2^50) - 0.5, 123456.5, 1/0, -1/0, 0/0}
local fs = {"band", "bor", "bxor", "btest", "bnot", "lshift", "rshift", "arshift", "lrotate", "rrotate", "extract", "replace"}
for _, f in ipairs(fs) do
  for _, a in ipairs(vals) do for _, b in ipairs(vals) do
    local ok, r = pcall(bit32[f], a, b)
    if not ok then r = "E" end
    local ok2, r2 = pcall(bit32[f], a, b, 3)
    if not ok2 then r2 = "E" end
    io.write(f, " ", tostring(r), " ", tostring(r2), "\n")
  end end
end
