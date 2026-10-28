namespace MoonSharp.Benchmarks
{
	/// <summary>
	/// Synthetic Lua 5.2 workloads. Each chunk returns the function the benchmark calls.
	/// </summary>
	public static class Workloads
	{
		public const string Eligibility = @"
return function(player, feature)
	if player.level < 10 then return false end
	if player.country ~= 'DE' and player.country ~= 'FR' then return false end
	if feature == 'offers' then return player.flags.beta == true end
	return true
end";

		public const string Fib = @"
local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end
return fib";

		public const string SumLoop = @"
return function(n)
	local s = 0
	for i = 1, n do s = s + i % 7 end
	return s
end";

		public const string TableWork = @"
return function(n)
	local t = {}
	for i = 1, n do t['k' .. i] = i end
	local s = 0
	for i = 1, n do s = s + t['k' .. i] end
	return s
end";

		public const string Concat = @"
return function(n)
	local parts = {}
	for i = 1, n do parts[#parts + 1] = tostring(i) end
	return table.concat(parts, ',')
end";

		// The string_format case of LuaWorkloads (n iterations, so bytes per call = alloc / n).
		public const string StringFormat = @"
local format = string.format
return function(n) local buf, j = {}, 1 for i = 1, n do buf[j] = format('%d:%s', i, 'x') j = j + 1 if j > 64 then j = 1 end end return buf[64] end";

		// A mix of the common string.format specifiers.
		public const string StringFormatMix = @"
local format = string.format
return function(n)
	local s
	for i = 1, n do s = format('%d:%s %5.2f %x %-6s|%g', i, 'x', i / 7, i, 'ab', i * 0.5) end
	return s
end";

		// The string_pattern case of LuaWorkloads: find with two captures, gsub of one character,
		// match anchored at the end.
		public const string StringPattern = @"
local s = 'the quick brown fox 123 jumps over the lazy dog 4567'
return function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end";

		// gsub with each kind of replacement: a string with a capture, a table, a function, and a
		// pattern that does not match (the result is the subject unchanged).
		public const string Gsub = @"
local s = 'the quick brown fox 123 jumps over the lazy dog 4567'
local t = { quick = 'slow', lazy = 'busy' }
local function len(w) return #w end
return function(n)
	local c = 0
	for i = 1, n do
		c = c + #s:gsub('(%w+)', '<%1>') + #s:gsub('%a+', t) + #s:gsub('%d+', len) + #s:gsub('%u', '')
	end
	return c
end";

		// gmatch over words and over key=value pairs, and the trim idiom.
		public const string Gmatch = @"
local s = 'the quick brown fox 123 jumps over the lazy dog 4567'
local kv = 'alpha=1, beta=22, gamma=333'
return function(n)
	local c = 0
	for i = 1, n do
		for w in s:gmatch('%a+') do c = c + #w end
		for k, v in kv:gmatch('(%w+)=(%w+)') do c = c + #k + #v end
		c = c + #(('   padded text   '):match('^%s*(.-)%s*$'))
	end
	return c
end";
	}
}
