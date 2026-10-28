-- Offer selection: filter a catalog by rules, then order by priority and price.
local eligibility = require("eligibility")

local M = {}

function M.select_offers(player, catalog, limit)
	if not eligibility.eligible(player, "offers") then
		return json.emptyarray()
	end

	local picked = {}

	for _, offer in ipairs(catalog) do
		local min_level = offer.min_level or 0
		if player.level >= min_level and (offer.countries == nil or offer.countries[player.country]) then
			picked[#picked + 1] = offer
		end
	end

	table.sort(picked, function(a, b)
		if a.priority ~= b.priority then
			return a.priority > b.priority
		end
		return a.price < b.price
	end)

	local result = {}
	for i = 1, math.min(limit, #picked) do
		result[i] = { id = picked[i].id, price = picked[i].price }
	end

	if #result == 0 then
		return json.emptyarray()
	end

	return result
end

return M
