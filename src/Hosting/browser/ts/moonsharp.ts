// TypeScript API for MoonSharp running on .NET WebAssembly in the browser.
// Scripts run locally: nothing is sent to a server.

export type ErrorKind = "syntax" | "runtime" | "input" | "output" | "host";

export interface HostError {
  kind: ErrorKind;
  message: string;
  chunk: string | null;
  line: number | null;
  stack: string[];
}

export type HostResult<T = unknown> =
  | { ok: true; value: T; output: string; timing: { compileMs?: number; runMs?: number } }
  | { ok: false; error: HostError; output: string };

export interface ContextOptions {
  /** Adds the int64 and uint64 modules (exact 64-bit integers). */
  int64?: boolean;
}

/** Arguments as JS values, or raw JSON text when a JS number cannot hold the value exactly. */
export type Args = unknown[] | { json: string };

interface Exports {
  Version(): string;
  CreateContext(int64: boolean): number;
  DisposeContext(context: number): boolean;
  Validate(context: number, chunk: string, source: string): string;
  Load(context: number, moduleId: string, source: string): string;
  Unload(context: number, moduleId: string): boolean;
  Invoke(context: number, moduleId: string, fn: string, argsJson: string): string;
  Run(context: number, chunk: string, source: string, inputJson: string | null): string;
}

/**
 * Serializes a value to JSON. bigint values become exact integer literals, so a 64-bit id given as
 * a bigint reaches the host unrounded (the host then rejects it if it exceeds 2^53).
 */
export function toJson(value: unknown): string {
  const marker = "\u0000bigint:";
  const text = JSON.stringify(value, (_key, v) => (typeof v === "bigint" ? marker + v.toString() : v));
  return text.replace(new RegExp(`"${marker}(-?\\d+)"`, "g"), "$1");
}

/** One independent Lua state. Modules loaded here are visible to require() in this context only. */
export class ScriptContext {
  private disposed = false;

  constructor(private readonly exports: Exports, readonly id: number) {}

  /** Compiles without running. */
  validate(chunk: string, source: string): HostResult<null> {
    return JSON.parse(this.live().Validate(this.id, chunk, source));
  }

  /** Runs a module once and keeps it for invoke() and require(). Loading the same id replaces it. */
  load<T = unknown>(moduleId: string, source: string): HostResult<T> {
    return JSON.parse(this.live().Load(this.id, moduleId, source));
  }

  unload(moduleId: string): boolean {
    return this.live().Unload(this.id, moduleId);
  }

  /** Calls a function exported by a loaded module. Compiled code is reused across calls. */
  invoke<T = unknown>(moduleId: string, fn: string, args: Args = []): HostResult<T> {
    const json = Array.isArray(args) ? toJson(args) : args.json;
    return JSON.parse(this.live().Invoke(this.id, moduleId, fn, json));
  }

  /** Compiles and runs a chunk; if it returns a function, calls it with the input. */
  run<T = unknown>(chunk: string, source: string, input?: unknown): HostResult<T> {
    return this.runJson(chunk, source, input === undefined ? null : toJson(input));
  }

  /** Like run, with the input as JSON text (or null for none), for values a JS number cannot hold exactly. */
  runJson<T = unknown>(chunk: string, source: string, inputJson: string | null): HostResult<T> {
    return JSON.parse(this.live().Run(this.id, chunk, source, inputJson));
  }

  dispose(): void {
    if (!this.disposed) {
      this.exports.DisposeContext(this.id);
      this.disposed = true;
    }
  }

  private live(): Exports {
    if (this.disposed) throw new Error(`script context ${this.id} is disposed`);
    return this.exports;
  }
}

export class MoonSharpRuntime {
  constructor(private readonly exports: Exports) {}

  get version(): string {
    return this.exports.Version();
  }

  /** A new Lua state. With int64: true it has the int64 and uint64 modules (docs/int64.md). */
  createContext(options: ContextOptions = {}): ScriptContext {
    return new ScriptContext(this.exports, this.exports.CreateContext(options.int64 ?? false));
  }
}

/**
 * Starts the .NET runtime and returns the API. frameworkUrl is the URL of the `_framework` folder
 * produced by `dotnet publish`, relative to the page (or to the worker script inside a Web Worker).
 */
export async function createMoonSharp(frameworkUrl = "./_framework"): Promise<MoonSharpRuntime> {
  const dotnetUrl = new URL(`${frameworkUrl}/dotnet.js`, globalThis.location.href).href;
  const { dotnet } = await import(/* @vite-ignore */ dotnetUrl);
  const runtime = await dotnet.create();
  const config = runtime.getConfig();
  const assemblyExports = await runtime.getAssemblyExports(config.mainAssemblyName);
  return new MoonSharpRuntime(assemblyExports.MoonSharp.Hosting.Browser.Exports as Exports);
}
