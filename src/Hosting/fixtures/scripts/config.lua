-- Builds a client configuration document from inputs. Exercises the JSON value mapping:
-- nested tables, empty arrays, Unicode text, non-integer numbers and IDs passed as strings.
local M = {}

function M.build(input)
	local cfg = {
		version = 3,
		player_id = input.player_id,          -- a 64-bit id, kept as a string
		greeting = "Merhaba, " .. input.name .. " 👋",
		ratio = 0.1 + 0.2,
		discount = input.price * 0.85,
		tags = {},
		empty_list = json.emptyarray(),
		passthrough = input.items,            -- an input array, possibly empty
		limits = { daily = 5, weekly = 20 },
	}

	for i, tag in ipairs(input.tags) do
		cfg.tags[i] = string.upper(tag)
	end

	if #cfg.tags == 0 then
		cfg.tags = json.emptyarray()
	end

	return cfg
end

function M.echo(value)
	return value
end

function M.fail(reason)
	error("cannot build config: " .. reason)
end

function M.count_args(...)
	return select("#", ...)
end

return M
