-- Loops of a chosen length, for the instruction limit cases in limits.suite.json.
local M = {}

function M.spin(n)
	local sum = 0
	for i = 1, n do
		sum = sum + i
	end
	return sum
end

return M
