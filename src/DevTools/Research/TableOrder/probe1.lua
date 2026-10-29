local function keys(t) local r = {} for k in pairs(t) do r[#r+1] = tostring(k) end return table.concat(r, ",") end
local t = { x = 1, y = 2 } t[1] = 'a' t[2] = 'b' t.z = 3
print("mixed:", keys(t))
print("json mixed obj:", json.serialize({ b = 1, a = 2, c = 3 }))
local u = {1, 2} u.x = 1 u.x = nil
local n = 0 for _ in pairs(u) do n = n + 1 end
print("pairs count after nil:", n)
local v = { a = 1, b = 2, c = 3 } v.a = nil v.a = 9
print("reset before collect:", keys(v))
local w = { a = 1, b = 2, c = 3 } w.a = nil w.d = 4 w.a = 9
print("reset after collect:", keys(w))
local z = {1,2,3} z[2] = nil z.x = 1 z[2] = 5
print("array hole refill:", keys(z), #z)
local h = {1,2,3} h[5] = 5
print("border:", #h)
local g = {} g[3] = 3 g[2] = 2 g[1] = 1
print("reverse fill:", keys(g), #g)
