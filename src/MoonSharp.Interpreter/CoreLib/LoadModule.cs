// Disable warnings about XML documentation
#pragma warning disable 1591

using System;
using System.Text;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing loading Lua functions like 'require', 'load', etc.
	/// </summary>
	[MoonSharpModule]
	public class LoadModule
	{
		public static void MoonSharpInit(Table globalTable, Table ioTable)
		{
			DynValue package = globalTable.Get("package");

			if (package.IsNil())
			{
				package = DynValue.NewTable(globalTable.OwnerScript);
				globalTable["package"] = package;
			}
			else if (package.Type != DataType.Table)
			{
				throw new InternalErrorException("'package' global variable was found and it is not a table");
			}

			string cfg = System.IO.Path.DirectorySeparatorChar + "\n;\n?\n!\n-\n";
			package.Table.Set("config", DynValue.NewString(cfg));

			// package.path holds the paths a script adds. It starts empty rather than as a copy of the loader's
			// ModulePaths, which hosts often set, or replace with a new loader, after the Script is created:
			// ScriptLoaderBase searches them after package.path.
			Script script = globalTable.OwnerScript;
			package.Table.Set("path", DynValue.NewString(""));
			package.Table.Set("preload", DynValue.NewTable(script));

			Table pkg = package.Table;
			DynValue searchers = DynValue.NewTable(script);
			searchers.Table.Set(1, DynValue.NewCallback((context, args) => SearchPreload(pkg, args), "searcher_preload"));
			searchers.Table.Set(2, DynValue.NewCallback(SearchLoader, "searcher_loader"));
			package.Table.Set("searchers", searchers);
			// The Lua 5.1 name, which Lua 5.2 keeps for compatibility.
			package.Table.Set("loaders", searchers);
		}

		// package.searchers[1]: the loader stored in package.preload[modname].
		static DynValue SearchPreload(Table package, CallbackArguments args)
		{
			string name = args.AsType(0, "require", DataType.String, false).String;
			DynValue preload = package.Get("preload");
			DynValue loader = preload.Type == DataType.Table ? preload.Table.Get(name) : DynValue.Nil;

			return loader.IsNil() ? DynValue.NewString("\n\tno field package.preload['" + name + "']") : loader;
		}

		// package.searchers[2]: a Lua chunk found by the script loader (through package.path and
		// ModulePaths for loaders based on ScriptLoaderBase). Its extra value is the file name.
		static DynValue SearchLoader(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string name = args.AsType(0, "require", DataType.String, false).String;
			Script S = executionContext.GetScript();
			IScriptLoader loader = S.Options.ScriptLoader;
			string file = loader.ResolveModuleName(name, S.Globals);

			if (file == null)
			{
				ScriptLoaderBase paths = loader as ScriptLoaderBase;

				return DynValue.NewString(paths != null
					? paths.DescribeMissingModule(name, S.Globals)
					: "\n\tno module '" + name + "' in " + loader.GetType().Name);
			}

			return DynValue.NewTuple(S.LoadFile(file, null, file), DynValue.NewString(file));
		}



		// load (ld [, source [, mode [, env]]])
		// ----------------------------------------------------------------
		// Loads a chunk.
		//
		// If ld is a string, the chunk is this string.
		//
		// If there are no syntactic errors, returns the compiled chunk as a function;
		// otherwise, returns nil plus the error message.
		//
		// source is used as the source of the chunk for error messages and debug
		// information (see §4.9). When absent, it defaults to ld, if ld is a string,
		// or to "=(load)" otherwise.
		//
		// The string mode is ignored, and assumed to be "t";
		[MoonSharpModuleMethod]
		public static DynValue load(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return load_impl(executionContext, args, null);
		}

		// loadsafe (ld [, source [, mode [, env]]])
		// ----------------------------------------------------------------
		// Same as load, except that "env" defaults to the current environment of the function
		// calling load, instead of the actual global environment.
		[MoonSharpModuleMethod]
		public static DynValue loadsafe(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return load_impl(executionContext, args, GetSafeDefaultEnv(executionContext));
		}

		public static DynValue load_impl(ScriptExecutionContext executionContext, CallbackArguments args, Table defaultEnv)
		{
			try
			{
				Script S = executionContext.GetScript();
				DynValue ld = args[0];
				string script = "";

				if (ld.Type == DataType.Function)
				{
					while (true)
					{
						DynValue ret = executionContext.GetScript().Call(ld);
						if (ret.Type == DataType.String && ret.String.Length > 0)
							script += ret.String;
						else if (ret.IsNil())
							break;
						else
							return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("reader function must return a string"));
					}
				}
				else if (ld.Type == DataType.String)
				{
					script = ld.String;
				}
				else
				{
					args.AsType(0, "load", DataType.Function, false);
				}

				DynValue source = args.AsType(1, "load", DataType.String, true);
				DynValue env = args.AsType(3, "load", DataType.Table, true);

				DynValue fn = S.LoadString(script,
					!env.IsNil() ? env.Table : defaultEnv,
					!source.IsNil() ? source.String : "=(load)");

				return fn;
			}
			catch (SyntaxErrorException ex)
			{
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(ex.DecoratedMessage ?? ex.Message));
			}
		}

		// loadfile ([filename [, mode [, env]]])
		// ----------------------------------------------------------------
		// Similar to load, but gets the chunk from file filename or from the standard input,
		// if no file name is given. INCOMPAT: stdin not supported, mode ignored
		[MoonSharpModuleMethod]
		public static DynValue loadfile(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return loadfile_impl(executionContext, args, null);
		}

		// loadfile ([filename [, mode [, env]]])
		// ----------------------------------------------------------------
		// Same as loadfile, except that "env" defaults to the current environment of the function
		// calling load, instead of the actual global environment.
		[MoonSharpModuleMethod]
		public static DynValue loadfilesafe(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			return loadfile_impl(executionContext, args, GetSafeDefaultEnv(executionContext));
		}



		private static DynValue loadfile_impl(ScriptExecutionContext executionContext, CallbackArguments args, Table defaultEnv)
		{
			try
			{
				Script S = executionContext.GetScript();
				DynValue filename = args.AsType(0, "loadfile", DataType.String, false);
				DynValue env = args.AsType(2, "loadfile", DataType.Table, true);

				DynValue fn = S.LoadFile(filename.String, env.IsNil() ? defaultEnv : env.Table);

				return fn;
			}
			catch (SyntaxErrorException ex)
			{
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(ex.DecoratedMessage ?? ex.Message));
			}
			catch (Exception ex) when (!(ex is InterpreterException))
			{
				return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(CannotOpen(ex, args[0].String)));
			}
		}


		private static Table GetSafeDefaultEnv(ScriptExecutionContext executionContext)
		{
			Table env = executionContext.CurrentGlobalEnv;

			if (env == null)
				throw new ScriptRuntimeException("current environment cannot be backtracked.");

			return env;
		}

		//dofile ([filename])
		//--------------------------------------------------------------------------------------------------------------
		//Opens the named file and executes its contents as a Lua chunk. When called without arguments,
		//dofile executes the contents of the standard input (stdin). Returns all values returned by the chunk.
		//In case of errors, dofile propagates the error to its caller (that is, dofile does not run in protected mode).
		[MoonSharpModuleMethod]
		public static DynValue dofile(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			try
			{
				Script S = executionContext.GetScript();
				DynValue v = args.AsType(0, "dofile", DataType.String, false);

				DynValue fn = S.LoadFile(v.String);

				return DynValue.NewTailCallReq(fn); // tail call to dofile
			}
			catch (SyntaxErrorException ex)
			{
				throw new ScriptRuntimeException(ex);
			}
			catch (Exception ex) when (!(ex is InterpreterException))
			{
				throw new ScriptRuntimeException(CannotOpen(ex, args[0].String));
			}
		}

		// The script loader could not read the file (FileSystemScriptLoader throws FileNotFoundException, the
		// Unity loader a plain Exception): an error a script can catch, with Lua 5.2's text.
		private static string CannotOpen(Exception ex, string filename)
		{
			return "cannot open " + filename + ": " + IoModule.IoErrorText(ex);
		}

		//require (modname)
		//----------------------------------------------------------------------------------------------------------------
		//Loads the given module. The function starts by looking into the package.loaded table to determine whether
		//modname is already loaded. If it is, then require returns the value stored at package.loaded[modname].
		//Otherwise, it tries to find a loader for the module.
		//
		//To find a loader, require is guided by the package.searchers sequence. By changing this sequence, we can
		//change how require looks for a module. The default configuration queries package.preload[modname] first,
		//then the script loader (see SearchLoader).
		//
		//Once a loader is found, require calls the loader with two arguments: modname and an extra value dependent on
		//how it got the loader. If the loader returns any non-nil value, require assigns the returned value to
		//package.loaded[modname]. If the loader does not return a non-nil value and has not assigned any value to
		//package.loaded[modname], then require assigns true to this entry. In any case, require returns the final
		//value of package.loaded[modname].
		//
		//If there is any error loading or running the module, or if it cannot find any loader for the module, then require
		//signals an error.
		//
		//__require_clr_impl returns the module when package.loaded has it. Otherwise it runs the searchers and returns
		//package.loaded, the loader and its extra value. require calls the loader in Lua, so that a module can yield
		//while it loads and require keeps working under pcall and when the host calls it.
		[MoonSharpModuleMethod]
		public static DynValue __require_clr_impl(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			Script S = executionContext.GetScript();
			DynValue name = args.AsType(0, "require", DataType.String, false);

			DynValue package = (executionContext.CurrentGlobalEnv ?? S.Globals).Get("package");

			if (package.Type != DataType.Table)
				throw RequireError(executionContext, "'package' must be a table");

			DynValue loaded = package.Table.Get("loaded");

			if (loaded.Type != DataType.Table)
			{
				loaded = DynValue.NewTable(S);
				package.Table.Set("loaded", loaded);
			}

			DynValue module = loaded.Table.Get(name);

			if (module.CastToBool())
				return module;

			DynValue searchers = package.Table.Get("searchers");

			if (searchers.Type != DataType.Table)
				throw RequireError(executionContext, "'package.searchers' must be a table");

			var notFound = new StringBuilder();

			for (int i = 1; ; i++)
			{
				DynValue searcher = searchers.Table.Get(i);

				if (searcher.IsNil())
					throw RequireError(executionContext, "module '" + name.String + "' not found:" + notFound);

				DynValue found = executionContext.Call(searcher, name);
				DynValue loader = found.ToScalar();

				if (loader.Type == DataType.Function || loader.Type == DataType.ClrFunction)
				{
					DynValue extra = (found.Type == DataType.Tuple && found.Tuple.Length > 1) ? found.Tuple[1] : DynValue.Nil;
					return DynValue.NewTuple(loaded, loader, extra);
				}

				if (loader.Type == DataType.String)
					notFound.Append(loader.String);
			}
		}

		// Reports an error of require at require's caller, as Lua does, rather than inside require's own code:
		// frame 0 is __require_clr_impl, frame 1 require, frame 2 its caller. A caller in C# (pcall(require, ...),
		// the host) has no position, as in Lua.
		static ScriptRuntimeException RequireError(ScriptExecutionContext executionContext, string message)
		{
			var e = new ScriptRuntimeException(message);
			WatchItem[] stack = executionContext.GetCallingCoroutine().GetStackTrace(0, executionContext.CallingLocation);

			if (stack.Length > 2 && stack[2].Location != null && !stack[2].Location.IsClrLocation)
				e.DecorateMessage(executionContext.GetScript(), stack[2].Location);
			else
				e.DoNotDecorateMessage = true;

			return e;
		}


		[MoonSharpModuleMethod]
		public const string require = @"
function(name)
	local loaded, loader, extra = __require_clr_impl(name)
	if loader == nil then
		return loaded
	end
	local m = loader(name, extra)
	if m ~= nil then
		loaded[name] = m
	end
	m = loaded[name]
	if m == nil then
		m = true
		loaded[name] = m
	end
	return m
end";



	}
}
