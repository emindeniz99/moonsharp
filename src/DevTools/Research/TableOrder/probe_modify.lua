-- next() while the table changes. Inserting new keys during traversal is undefined in Lua.
local t = { a = 1, b = 2, c = 3 }
local n = 0
local ok, err = pcall(function()
	for k in pairs(t) do n = n + 1 t[k .. 'x'] = 1 if n > 1000 then break end end
end)
print('insert during pairs: steps', n, ok and 'ok' or err)
local u = {} for i = 1, 8 do u['k' .. i] = i end
local m = 0
ok, err = pcall(function()
	for k in pairs(u) do m = m + 1 u[k] = nil u['n' .. m] = m if m > 1000 then break end end
end)
print('delete current + insert new during pairs: steps', m, ok and 'ok' or err)
local w = {} for i = 1, 8 do w['k' .. i] = i end
local c = 0
for k in pairs(w) do c = c + 1 w[k] = nil end
print('clear all during pairs: steps', c, 'left', next(w))
local a = {} for i = 1, 8 do a[i] = i end
local s = 0
ok, err = pcall(function() for k, v in pairs(a) do s = s + 1 a[#a + 1] = v if s > 1000 then break end end end)
print('append during pairs of a sequence: steps', s, ok and 'ok' or err)
