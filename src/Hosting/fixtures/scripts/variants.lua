-- Deterministic A/B bucketing. The hash uses only integer arithmetic below 2^53,
-- so every host computes the same bucket from the same user id.
local M = {}

local function hash(s)
	local h = 7
	for i = 1, #s do
		h = (h * 31 + string.byte(s, i)) % 4294967296
	end
	return h
end

M.hash = hash

-- experiment = { name = "...", variants = { { name = "a", weight = 50 }, ... } }
function M.select_variant(user_id, experiment)
	local total = 0
	for _, v in ipairs(experiment.variants) do
		total = total + v.weight
	end

	local bucket = hash(experiment.name .. ":" .. user_id) % total
	local acc = 0

	for _, v in ipairs(experiment.variants) do
		acc = acc + v.weight
		if bucket < acc then
			return { experiment = experiment.name, variant = v.name, bucket = bucket }
		end
	end
end

return M
