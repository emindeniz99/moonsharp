-- json.serialize key order under each variant (object keys come from pairs order)
print(json.serialize({ name = 'offer', price = 10, currency = 'EUR', tags = { 'a', 'b' } }))
local o = {} o.zeta = 1 o.alpha = 2 o.mid = 3
print(json.serialize(o))
local p = json.parse('{"b":1,"a":2,"c":{"y":1,"x":2}}')
print(json.serialize(p))
local q = { a = 1, b = 2, c = 3 } q.a = nil q.d = 4 q.a = 5
print(json.serialize(q))
-- a mixed table: the core json module drops non-string keys from objects, the hosting LuaJson does not
local m = { x = 1 } m[2] = 'two'
local ks = {} for k in pairs(m) do ks[#ks + 1] = tostring(k) end
print('mixed pairs:', table.concat(ks, ','), 'core json:', json.serialize(m))
