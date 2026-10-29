-- Differential fuzz: inserts and updates only (no deletion), so pairs order must match master exactly in (a).
local seed = 777
local function rnd(n) seed = (seed * 1103515245 + 12345) % 2147483648 return seed % n end

local function key()
	local r = rnd(100)
	if r < 45 then return rnd(40) + 1 end
	if r < 55 then return rnd(300) + 41 end
	if r < 60 then return -rnd(5) end
	if r < 65 then return rnd(20) + 0.5 end
	return 's' .. rnd(40)
end

local function order(t)
	local items = {}
	for k in pairs(t) do items[#items + 1] = tostring(k) end
	return #t .. ' | ' .. table.concat(items, ' ')
end

for round = 1, 60 do
	local t = {}
	if rnd(2) == 0 then t = { 'p', 'q', x = 1, 'r' } end
	for op = 1, 300 do
		local r = rnd(100)
		if r < 80 then t[key()] = op
		elseif r < 90 then table.insert(t, op)
		else t[#t + 1] = op end
		if op % 50 == 0 then print(round, op, order(t)) end
	end
end
