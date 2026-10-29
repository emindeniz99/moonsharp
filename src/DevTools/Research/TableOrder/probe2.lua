local function keys(t) local r = {} for k in pairs(t) do r[#r+1] = tostring(k) end return table.concat(r, ",") end
local t = { a = 1, b = 2, c = 3 }
t.a = nil t.b = nil t.d = 4 t.b = 5 t.a = 6
print("two deaths, insert, revive both:", keys(t))
local u = { a = 1, b = 2, c = 3 }
u.zz = nil u.d = 4 u.zz = 5
print("nil on absent key then insert:", keys(u))
local w = {}
for i = 1, 6 do w['k' .. i] = i end
for i = 1, 6 do w['k' .. i] = nil end
w.x = 1
for i = 6, 1, -1 do w['k' .. i] = i end
print("clear all, insert, refill reversed:", keys(w))
