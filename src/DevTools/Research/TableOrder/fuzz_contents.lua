-- Differential fuzz: contents and #t must match master in every variant (order is ignored here).
local seed = 12345
local function rnd(n) seed = (seed * 1103515245 + 12345) % 2147483648 return seed % n end

local function key()
	local r = rnd(100)
	if r < 45 then return rnd(40) + 1 end            -- dense integer keys
	if r < 55 then return rnd(300) + 41 end          -- sparse integer keys
	if r < 60 then return -rnd(5) end                -- 0 and negatives
	if r < 65 then return rnd(20) + 0.5 end          -- fractional
	if r < 67 then return rnd(2) == 0 end            -- booleans
	return 's' .. rnd(40)                             -- strings
end

local function dump(t)
	local items = {}
	for k, v in pairs(t) do items[#items + 1] = type(k) .. ':' .. tostring(k) .. '=' .. tostring(v) end
	table.sort(items)
	return #t .. ' | ' .. table.concat(items, ' ')
end

local total = 0
for round = 1, 60 do
	local t = {}
	for op = 1, 400 do
		local r = rnd(100)
		if r < 60 then
			t[key()] = op
		elseif r < 80 then
			t[key()] = nil
		elseif r < 85 then
			table.insert(t, op)
		elseif r < 90 then
			if #t > 0 then table.remove(t, rnd(#t) + 1) end
		elseif r < 93 then
			-- clear some fields during a traversal (allowed by the manual)
			for k in pairs(t) do if (#tostring(k) + op) % 3 == 0 then t[k] = nil end end
		elseif r < 95 then
			-- next() from every live key must work
			local n = 0
			local k = next(t)
			while k ~= nil do n = n + 1 k = next(t, k) end
			local m = 0 for _ in pairs(t) do m = m + 1 end
			if n ~= m then print('next/pairs count mismatch', n, m) end
		else
			t[#t + 1] = op
		end
		if op % 50 == 0 then print(round, op, dump(t)) total = total + 1 end
	end
end
print('checkpoints', total)
