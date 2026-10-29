-- A sequence whose extra field was cleared: Table.Pairs on master still yields the dead key.
local u = { 10, 20 }
u.x = 1
u.x = nil
return u
