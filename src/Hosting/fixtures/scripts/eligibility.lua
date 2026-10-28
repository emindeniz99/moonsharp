-- Feature eligibility rules: a typical predicate evaluated on every request.
local M = {}

local allowed_countries = { DE = true, FR = true, TR = true }

function M.eligible(player, feature)
	if player.level < 10 then
		return false
	end

	if not allowed_countries[player.country] then
		return false
	end

	if feature == "offers" then
		return player.flags ~= nil and player.flags.beta == true
	end

	return true
end

return M
