using System;
using System.Runtime.InteropServices.JavaScript;
using MoonSharp.Interpreter;

public static partial class Smoke
{
    public static void Main()
    {
    }

    /// <summary>
    /// Creates a script with every core module and runs a snippet that needs the standard
    /// library. Errors are returned as text so the page can show them.
    /// </summary>
    [JSExport]
    public static string Run()
    {
        return Execute("return string.upper('ok') .. table.concat({1,2}, ',') .. type(require)");
    }

    /// <summary>
    /// Wider check: every module exposes its functions, the userdata the library registers
    /// itself still has its members, and stdin (unsupported in the browser) fails only when used.
    /// Returns "ok" or the list of problems.
    /// </summary>
    [JSExport]
    public static string RunLibraries()
    {
        return Execute(@"
            local problems = {}
            local function need(path)
                local v = _G
                for part in path:gmatch('[^.]+') do
                    v = type(v) == 'table' and v[part] or nil
                end
                if v == nil then problems[#problems + 1] = path end
            end

            for _, path in ipairs {
                'assert', 'error', 'ipairs', 'pairs', 'next', 'pcall', 'select', 'setmetatable', 'tostring', 'tonumber',
                'load', 'loadsafe', 'require', 'dofile', 'xpcall', 'rawget', 'print',
                'string.format', 'string.gsub', 'string.rep', 'table.sort', 'table.insert', 'math.floor', 'math.random',
                'os.time', 'os.date', 'os.clock', 'coroutine.wrap', 'coroutine.create', 'bit32.band',
                'json.parse', 'json.serialize', 'debug.traceback', 'dynamic.eval', 'io.open', 'io.read', 'io.write',
                'io.stdin', 'io.stdout', 'io.stderr', 'package.loaded',
            } do need(path) end

            -- members of the userdata types the library registers itself
            if type(io.stdout.write) ~= 'function' then problems[#problems + 1] = 'file:write' end
            if type(io.stdout.read) ~= 'function' then problems[#problems + 1] = 'file:read' end
            if type(json.null().isNull) ~= 'function' then problems[#problems + 1] = 'jsonnull.isNull' end

            -- stdin is unavailable in the browser: using it must fail with a catchable error
            local ok, err = pcall(io.read)
            if ok then problems[#problems + 1] = 'io.read succeeded without stdin' end

            -- Lua-source library functions and plain calls
            local sum = 0
            for _, v in ipairs { 1, 2, 3 } do sum = sum + v end
            if sum ~= 6 then problems[#problems + 1] = 'ipairs' end
            if json.serialize({ 1, 2 }) ~= '[1,2]' then problems[#problems + 1] = 'json.serialize' end

            if #problems == 0 then return 'ok' end
            return 'missing: ' .. table.concat(problems, ', ')
        ");
    }

    private static string Execute(string code)
    {
        try
        {
            Script script = new Script(CoreModules.Preset_Complete);
            return script.DoString(code).CastToString();
        }
        catch (Exception ex)
        {
            string text = "ERROR";
            for (Exception e = ex; e != null; e = e.InnerException)
                text += " " + e.GetType().Name + ": " + e.Message + " <-";
            return text;
        }
    }
}
