using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter;

namespace MoonSharp.LuaCats
{
	/// <summary>
	/// The standard library part of the definitions. lua-language-server can only turn a library on or off as a
	/// whole, while MoonSharp's module flags split some of them (the global functions over Basic, LoadMethods,
	/// Metatables and ErrorHandling; os over OS_Time and OS_System). So the global functions, os and package are
	/// always declared here, function by function, for what the script has; the other libraries are kept from
	/// lua-language-server when present and turned off when absent.
	/// </summary>
	internal sealed class StandardLibrary
	{
		// Lua 5.2 members of the libraries lua-language-server keeps when the script has them.
		static readonly Dictionary<string, string[]> s_Builtin = new Dictionary<string, string[]>
		{
			{ "bit32", new[] { "arshift", "band", "bnot", "bor", "btest", "bxor", "extract", "lrotate", "lshift", "replace", "rrotate", "rshift" } },
			{ "coroutine", new[] { "create", "resume", "running", "status", "wrap", "yield" } },
			{ "debug", new[] { "debug", "gethook", "getinfo", "getlocal", "getmetatable", "getregistry", "getupvalue", "getuservalue", "sethook", "setlocal", "setmetatable", "setupvalue", "setuservalue", "traceback", "upvalueid", "upvaluejoin" } },
			{ "io", new[] { "close", "flush", "input", "lines", "open", "output", "popen", "read", "stderr", "stdin", "stdout", "tmpfile", "type", "write" } },
			{ "math", new[] { "abs", "acos", "asin", "atan", "atan2", "ceil", "cos", "cosh", "deg", "exp", "floor", "fmod", "frexp", "huge", "ldexp", "log", "max", "min", "modf", "pi", "pow", "rad", "random", "randomseed", "sin", "sinh", "sqrt", "tan", "tanh" } },
			{ "string", new[] { "byte", "char", "dump", "find", "format", "gmatch", "gsub", "len", "lower", "match", "rep", "reverse", "sub", "upper" } },
			{ "table", new[] { "concat", "insert", "pack", "remove", "sort", "unpack" } },
		};

		// Declared here, so lua-language-server's versions are always off.
		static readonly string[] s_Own = { "basic", "os", "package" };

		// Modules declared here when present: Lua 5.2's os and package, and MoonSharp's json and dynamic.
		static readonly string[] s_Modules = { "dynamic", "json", "os", "package" };

		static readonly string[] s_Values = { "_G", "_MOONSHARP", "_VERSION", "int64", "uint64" };

		static readonly Dictionary<string, string> s_Functions = new Dictionary<string, string>
		{
			// Global functions of Lua 5.2 (loadstring from its default build) and MoonSharp (loadsafe, loadfilesafe, pack,
			// unpack).
			{ "assert", "---@generic T\n---@param v T\n---@param message? any\n---@param ... any\n---@return T\n---@return any ...\nfunction assert(v, message, ...) end\n" },
			{ "collectgarbage", "---@param opt? \"collect\"|\"count\"|\"step\"|\"isrunning\"|\"stop\"|\"restart\"|\"setpause\"|\"setstepmul\"|\"setmajorinc\"|\"generational\"|\"incremental\"\n---@param arg? integer\n---@return any ...\nfunction collectgarbage(opt, arg) end\n" },
			{ "dofile", "---@param filename? string\n---@return any ...\nfunction dofile(filename) end\n" },
			{ "error", "---@param message any\n---@param level? integer\nfunction error(message, level) end\n" },
			{ "getmetatable", "---@param object any\n---@return table metatable\nfunction getmetatable(object) end\n" },
			{ "ipairs", "---@generic T: table, V\n---@param t T\n---@return fun(table: V[], i?: integer): integer, V\n---@return T\n---@return integer i\nfunction ipairs(t) end\n" },
			{ "load", "---@param chunk string|function\n---@param chunkname? string\n---@param mode? string\n---@param env? table\n---@return function?\n---@return string? error_message\nfunction load(chunk, chunkname, mode, env) end\n" },
			{ "loadfile", "---@param filename? string\n---@param mode? string\n---@param env? table\n---@return function?\n---@return string? error_message\nfunction loadfile(filename, mode, env) end\n" },
			{ "loadfilesafe", "---@param filename? string\n---@param mode? string\n---@param env? table\n---@return function?\n---@return string? error_message\nfunction loadfilesafe(filename, mode, env) end\n" },
			{ "loadsafe", "---@param chunk string|function\n---@param chunkname? string\n---@param mode? string\n---@param env? table\n---@return function?\n---@return string? error_message\nfunction loadsafe(chunk, chunkname, mode, env) end\n" },
			{ "loadstring", "---@param chunk string|function\n---@param chunkname? string\n---@param mode? string\n---@param env? table\n---@return function?\n---@return string? error_message\nfunction loadstring(chunk, chunkname, mode, env) end\n" },
			{ "next", "---@generic K, V\n---@param table table<K, V>\n---@param index? K\n---@return K?\n---@return V?\nfunction next(table, index) end\n" },
			{ "pack", "---@param ... any\n---@return table\nfunction pack(...) end\n" },
			{ "pairs", "---@generic T: table, K, V\n---@param t T\n---@return fun(table: table<K, V>, index?: K): K, V\n---@return T\nfunction pairs(t) end\n" },
			{ "pcall", "---@param f function\n---@param ... any\n---@return boolean success\n---@return any ...\nfunction pcall(f, ...) end\n" },
			{ "print", "---@param ... any\nfunction print(...) end\n" },
			{ "rawequal", "---@param v1 any\n---@param v2 any\n---@return boolean\nfunction rawequal(v1, v2) end\n" },
			{ "rawget", "---@param table table\n---@param index any\n---@return any\nfunction rawget(table, index) end\n" },
			{ "rawlen", "---@param v table|string\n---@return integer\nfunction rawlen(v) end\n" },
			{ "rawset", "---@param table table\n---@param index any\n---@param value any\n---@return table\nfunction rawset(table, index, value) end\n" },
			{ "require", "---@param modname string\n---@return any\nfunction require(modname) end\n" },
			{ "select", "---@param index integer|\"#\"\n---@param ... any\n---@return any\nfunction select(index, ...) end\n" },
			{ "setmetatable", "---@param table table\n---@param metatable? table\n---@return table\nfunction setmetatable(table, metatable) end\n" },
			{ "tonumber", "---@param e any\n---@param base? integer\n---@return number?\nfunction tonumber(e, base) end\n" },
			{ "tostring", "---@param v any\n---@return string\nfunction tostring(v) end\n" },
			{ "type", "---@param v any\n---@return string\nfunction type(v) end\n" },
			{ "unpack", "---@generic T\n---@param list T[]\n---@param i? integer\n---@param j? integer\n---@return T ...\nfunction unpack(list, i, j) end\n" },
			{ "xpcall", "---@param f function\n---@param msgh function\n---@param ... any\n---@return boolean success\n---@return any ...\nfunction xpcall(f, msgh, ...) end\n" },

			{ "_G", "---@type table<string, any>\n_G = {}\n" },
			{ "_VERSION", "---@type string\n_VERSION = nil\n" },
			{ "_MOONSHARP", "---@class moonsharp.info\n---@field version string\n---@field luacompat string\n---@field platform string\n---@field is_aot boolean\n---@field is_unity boolean\n---@field is_mono boolean\n---@field is_clr4 boolean\n---@field banner string\n\n---@type moonsharp.info\n_MOONSHARP = nil\n" },

			{ "math.log10", "---@param x number\n---@return number\nfunction math.log10(x) end\n" },

			{ "table.maxn", "---@param table table\n---@return number\nfunction table.maxn(table) end\n" },

			{ "os.clock", "---@return number\nfunction os.clock() end\n" },
			{ "os.date", "---@param format? string\n---@param time? number\n---@return string|table\nfunction os.date(format, time) end\n" },
			{ "os.difftime", "---@param t2 number\n---@param t1? number\n---@return number\nfunction os.difftime(t2, t1) end\n" },
			{ "os.execute", "---@param command? string\n---@return boolean? suc\n---@return string? exitcode\n---@return integer? code\nfunction os.execute(command) end\n" },
			{ "os.exit", "---@param code? boolean|integer\n---@param close? boolean\nfunction os.exit(code, close) end\n" },
			{ "os.getenv", "---@param varname string\n---@return string?\nfunction os.getenv(varname) end\n" },
			{ "os.remove", "---@param filename string\n---@return boolean? suc\n---@return string? errmsg\nfunction os.remove(filename) end\n" },
			{ "os.rename", "---@param oldname string\n---@param newname string\n---@return boolean? suc\n---@return string? errmsg\nfunction os.rename(oldname, newname) end\n" },
			{ "os.setlocale", "---@param locale? string\n---@param category? string\n---@return string?\nfunction os.setlocale(locale, category) end\n" },
			{ "os.time", "---@param date? table\n---@return number\nfunction os.time(date) end\n" },
			{ "os.tmpname", "---@return string\nfunction os.tmpname() end\n" },

			{ "package.config", "---@type string\npackage.config = nil\n" },
			{ "package.loaded", "---@type table<string, any>\npackage.loaded = nil\n" },

			{ "string.contains", "---@param s string\n---@param part string\n---@return boolean\nfunction string.contains(s, part) end\n" },
			{ "string.endsWith", "---@param s string\n---@param suffix string\n---@return boolean\nfunction string.endsWith(s, suffix) end\n" },
			{ "string.startsWith", "---@param s string\n---@param prefix string\n---@return boolean\nfunction string.startsWith(s, prefix) end\n" },
			{ "string.unicode", "---UTF-16 code units, where string.byte gives 63 above 255.\n---@param s string\n---@param i? integer\n---@param j? integer\n---@return integer ...\nfunction string.unicode(s, i, j) end\n" },

			{ "json.emptyarray", "---A value that json.serialize writes as [].\n---@return userdata\nfunction json.emptyarray() end\n" },
			{ "json.isemptyarray", "---@param value any\n---@return boolean\nfunction json.isemptyarray(value) end\n" },
			{ "json.isnull", "---@param value any\n---@return boolean\nfunction json.isnull(value) end\n" },
			{ "json.null", "---A value that json.serialize writes as null.\n---@return userdata\nfunction json.null() end\n" },
			{ "json.parse", "---@param text string\n---@param emptyArrays? boolean\n---@param bigIntegers? \"number\"|\"int64\"|\"string\"\n---@return any\nfunction json.parse(text, emptyArrays, bigIntegers) end\n" },
			{ "json.serialize", "---@param value table\n---@return string\nfunction json.serialize(value) end\n" },

			{ "dynamic.eval", "---@param expr string|userdata\n---@return any\nfunction dynamic.eval(expr) end\n" },
			{ "dynamic.prepare", "---@param expr string\n---@return userdata\nfunction dynamic.prepare(expr) end\n" },
		};

		const string Int64Types = @"---An exact signed 64-bit integer (CoreModules.Int64, see docs/int64.md in MoonSharp).
---@class int64
---@operator add(int64|number): int64
---@operator sub(int64|number): int64
---@operator mul(int64|number): int64
---@operator div(int64|number): int64
---@operator mod(int64|number): int64
---@operator unm: int64
---@operator concat(string|number): string

---An exact unsigned 64-bit integer.
---@class uint64
---@operator add(uint64|number): uint64
---@operator sub(uint64|number): uint64
---@operator mul(uint64|number): uint64
---@operator div(uint64|number): uint64
---@operator mod(uint64|number): uint64
---@operator unm: uint64
---@operator concat(string|number): string
";

		const string Int64Module = @"---@class int64lib
---@overload fun(v: int64|uint64|number|string): int64
---@field max int64
---@field min int64
local int64lib = {}

---@param v any
---@return boolean
function int64lib.is(v) end

---@param x int64|number
---@return string
function int64lib.tostring(x) end

---@param x int64|number
---@return number?
---@return string? message
function int64lib.tonumber(x) end

---@param a int64|uint64|number
---@param b int64|uint64|number
---@return boolean
function int64lib.eq(a, b) end

---@param a int64|number
---@param b int64|number
---@param ... int64|number
---@return int64
function int64lib.band(a, b, ...) end

---@param a int64|number
---@param b int64|number
---@param ... int64|number
---@return int64
function int64lib.bor(a, b, ...) end

---@param a int64|number
---@param b int64|number
---@param ... int64|number
---@return int64
function int64lib.bxor(a, b, ...) end

---@param x int64|number
---@return int64
function int64lib.bnot(x) end

---@param x int64|number
---@param n number
---@return int64
function int64lib.shl(x, n) end

---@param x int64|number
---@param n number
---@return int64
function int64lib.shr(x, n) end

---@param x int64|number
---@param n number
---@return int64
function int64lib.sar(x, n) end

---@type int64lib
int64 = nil
";

		const string UInt64Module = @"---@class uint64lib
---@overload fun(v: int64|uint64|number|string): uint64
---@field max uint64
local uint64lib = {}

---@param v any
---@return boolean
function uint64lib.is(v) end

---@param x uint64|number
---@return string
function uint64lib.tostring(x) end

---@param x uint64|number
---@return number?
---@return string? message
function uint64lib.tonumber(x) end

---@param a int64|uint64|number
---@param b int64|uint64|number
---@return boolean
function uint64lib.eq(a, b) end

---@param a uint64|number
---@param b uint64|number
---@param ... uint64|number
---@return uint64
function uint64lib.band(a, b, ...) end

---@param a uint64|number
---@param b uint64|number
---@param ... uint64|number
---@return uint64
function uint64lib.bor(a, b, ...) end

---@param a uint64|number
---@param b uint64|number
---@param ... uint64|number
---@return uint64
function uint64lib.bxor(a, b, ...) end

---@param x uint64|number
---@return uint64
function uint64lib.bnot(x) end

---@param x uint64|number
---@param n number
---@return uint64
function uint64lib.shl(x, n) end

---@param x uint64|number
---@param n number
---@return uint64
function uint64lib.shr(x, n) end

---@type uint64lib
uint64 = nil
";

		static readonly Dictionary<string, string[]> s_Int64Members = new Dictionary<string, string[]>
		{
			{ "int64", new[] { "band", "bnot", "bor", "bxor", "eq", "is", "max", "min", "sar", "shl", "shr", "tonumber", "tostring" } },
			{ "uint64", new[] { "band", "bnot", "bor", "bxor", "eq", "is", "max", "shl", "shr", "tonumber", "tostring" } },
		};

		readonly Script m_Script;
		readonly Emitter m_Emitter;

		public StandardLibrary(Script script, Emitter emitter)
		{
			m_Script = script;
			m_Emitter = emitter;
		}

		/// <summary>The lua-language-server libraries the .luarc.json turns off, in name order.</summary>
		public List<string> DisabledBuiltins = new List<string>();

		/// <summary>True for the global names this part declares (or deliberately leaves to lua-language-server).</summary>
		public bool Handles(string global)
		{
			return s_Builtin.ContainsKey(global) || s_Modules.Contains(global) || s_Values.Contains(global)
				|| s_Functions.ContainsKey(global);
		}

		public string Emit()
		{
			var sb = new StringBuilder();
			Table g = m_Script.Globals;

			foreach (string name in s_Functions.Keys.Where(k => k.IndexOf('.') < 0).OrderBy(k => k, StringComparer.Ordinal))
				if (g.Get(name).IsNotNil())
					sb.Append(s_Functions[name]).Append('\n');

			foreach (string module in s_Modules)
			{
				DynValue t = g.Get(module);
				if (t.Type != DataType.Table)
					continue;

				// A class, so that lua-language-server reports the functions the module does not have.
				sb.Append("---@class ").Append(module).Append("lib\n").Append(module).Append(" = {}\n\n");

				foreach (string key in Keys(t.Table))
				{
					string text;
					if (s_Functions.TryGetValue(module + "." + key, out text))
						sb.Append(text).Append('\n');
					else
						m_Emitter.EmitValue(sb, module + "." + key, t.Table.Get(key), new HashSet<Table>());
				}
			}

			if (m_Emitter.Int64)
				sb.Append(Int64Types).Append('\n');

			foreach (var lib in s_Int64Members)
			{
				DynValue t = g.Get(lib.Key);
				if (t.Type != DataType.Table)
					continue;

				sb.Append(lib.Key == "int64" ? Int64Module : UInt64Module).Append('\n');

				foreach (string key in Keys(t.Table).Except(lib.Value))
					m_Emitter.EmitValue(sb, lib.Key + "." + key, t.Table.Get(key), new HashSet<Table>());
			}

			foreach (var lib in s_Builtin.OrderBy(l => l.Key, StringComparer.Ordinal))
			{
				DynValue t = g.Get(lib.Key);

				if (t.Type != DataType.Table)
				{
					DisabledBuiltins.Add(lib.Key);
					continue;
				}

				foreach (string missing in lib.Value.Where(k => Get(t.Table, k).IsNil()))
					m_Emitter.Warn(string.Format("lua-language-server still offers {0}.{1}, which this script does not have", lib.Key, missing));

				foreach (string key in Keys(t.Table).Except(lib.Value))
				{
					string text;
					if (s_Functions.TryGetValue(lib.Key + "." + key, out text))
						sb.Append(text).Append('\n');
					else
						m_Emitter.EmitValue(sb, lib.Key + "." + key, t.Table.Get(key), new HashSet<Table>());
				}
			}

			DisabledBuiltins.AddRange(s_Own);
			DisabledBuiltins.Sort(StringComparer.Ordinal);
			return sb.ToString();
		}

		static IEnumerable<string> Keys(Table t)
		{
			return t.Keys.Where(k => k.Type == DataType.String && Emitter.IsIdentifier(k.String))
				.Select(k => k.String).OrderBy(k => k, StringComparer.Ordinal).ToList();
		}

		/// <summary>A field, also through __index (io.stdout and the other standard files come from an __index
		/// function).</summary>
		DynValue Get(Table t, string key)
		{
			DynValue v = t.Get(key);
			DynValue index = (t.MetaTable != null) ? t.MetaTable.Get("__index") : DynValue.Nil;

			if (v.IsNil() && index.Type == DataType.Table)
				v = index.Table.Get(key);
			else if (v.IsNil() && (index.Type == DataType.Function || index.Type == DataType.ClrFunction))
				v = m_Script.Call(index, t, key);

			return v;
		}
	}
}
