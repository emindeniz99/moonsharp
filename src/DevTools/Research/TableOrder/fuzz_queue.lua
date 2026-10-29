-- Queues and stacks over integer keys: contents, #t and pairs order (order compared for a only).
local function dump(t)
	local items = {}
	for k, v in pairs(t) do items[#items + 1] = tostring(k) .. '=' .. tostring(v) end
	return #t .. ' | ' .. table.concat(items, ' ')
end
local q, head, tail = { name = 'q' }, 1, 1
for i = 1, 3000 do
	q[tail] = i tail = tail + 1
	if i % 3 ~= 0 then q[head] = nil head = head + 1 end
	if i % 500 == 0 then print('queue', i, dump(q)) end
end
local s = {}
for i = 1, 3000 do
	s[#s + 1] = i
	if i % 4 == 0 then s[#s] = nil s[#s] = nil end
	if i % 500 == 0 then print('stack', i, dump(s)) end
end
local f = {}
for i = 1, 200 do f[i] = i end
for i = 1, 200, 2 do f[i] = nil end
f.x = 1
for i = 201, 260 do f[i] = i end
print('holes', dump(f))
