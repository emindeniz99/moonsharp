-- Hosting LuaJson writes integer keys of a non-array table as object keys, in pairs order.
local m = { name = 'cfg' }
m[2] = 'two'
m.after = true
local o = {} o.zeta = 1 o.alpha = 2
return { mixed = m, plain = o }
