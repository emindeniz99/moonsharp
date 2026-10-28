// Runs the shared fixture suites (src/Hosting/fixtures) with the same rules as the .NET and Java runners.
import { MoonSharpRuntime, HostResult, ScriptContext, toJson } from "./moonsharp.js";

export interface CaseReport { name: string; pass: boolean; detail: string | null }
export interface SuiteReport { suite: string; cases: CaseReport[] }

interface Case {
  name: string; op?: string; module?: string; function?: string;
  args?: unknown[]; argsJson?: string; expect?: unknown;
  expectError?: { kind?: string; messageContains?: string; line?: number };
  chunk?: string; source?: string; input?: unknown; inputJson?: string;
}

export async function runSuite(runtime: MoonSharpRuntime, suiteUrl: string): Promise<SuiteReport> {
  const suite = await (await fetch(suiteUrl)).json();
  const base = new URL(".", new URL(suiteUrl, location.href));
  const context = runtime.createContext({ int64: suite.context?.int64 ?? false });
  const cases: CaseReport[] = [];

  try {
    for (const [id, path] of Object.entries((suite.modules ?? {}) as Record<string, string>)) {
      const source = await (await fetch(new URL(path, base))).text();
      const loaded = context.load(id, source);
      if (!loaded.ok) cases.push({ name: `load ${id}`, pass: false, detail: JSON.stringify(loaded.error) });
    }

    for (const c of suite.cases as Case[]) {
      const detail = check(c, execute(context, c));
      cases.push({ name: c.name, pass: detail === null, detail });
    }
  } finally {
    context.dispose();
  }

  return { suite: suite.suite, cases };
}

function execute(context: ScriptContext, c: Case): HostResult {
  switch (c.op ?? "invoke") {
    case "invoke":
      return context.invoke(c.module!, c.function!, c.argsJson !== undefined ? { json: c.argsJson } : c.args ?? []);
    case "validate":
      return context.validate(c.chunk ?? c.name, c.source!);
    case "run":
      return context.runJson(c.chunk ?? c.name, c.source!, c.inputJson ?? (c.input === undefined ? null : toJson(c.input)));
    default:
      throw new Error(`unknown op ${c.op}`);
  }
}

export function check(c: Case, r: HostResult): string | null {
  if (!c.expectError) {
    if (!r.ok) return `unexpected error: ${JSON.stringify(r.error)}`;
    return diff(c.expect, r.value, "$");
  }
  if (r.ok) return `expected an error, got ${JSON.stringify(r.value)}`;
  const e = c.expectError;
  if (e.kind !== undefined && e.kind !== r.error.kind) return `error kind: expected ${e.kind}, got ${r.error.kind} (${r.error.message})`;
  if (e.messageContains !== undefined && !r.error.message.includes(e.messageContains))
    return `error message: expected to contain "${e.messageContains}", got "${r.error.message}"`;
  if (e.line !== undefined && e.line !== r.error.line) return `error line: expected ${e.line}, got ${r.error.line}`;
  return null;
}

/** Structural comparison: object key order is ignored, numbers compare by value. */
export function diff(expected: unknown, actual: unknown, path: string): string | null {
  const show = (v: unknown) => JSON.stringify(v ?? null);
  const kind = (v: unknown) => (v === null || v === undefined ? "null" : Array.isArray(v) ? "array" : typeof v);

  if (kind(expected) !== kind(actual)) return `${path}: expected ${show(expected)}, got ${show(actual)}`;

  if (Array.isArray(expected)) {
    const a = actual as unknown[];
    if (expected.length !== a.length) return `${path}: expected ${expected.length} items, got ${a.length} (${show(actual)})`;
    for (let i = 0; i < expected.length; i++) {
      const d = diff(expected[i], a[i], `${path}[${i}]`);
      if (d) return d;
    }
    return null;
  }

  if (kind(expected) === "object") {
    const e = expected as Record<string, unknown>, a = actual as Record<string, unknown>;
    for (const k of Object.keys(e)) {
      if (!(k in a)) return `${path}.${k}: missing (expected ${show(e[k])})`;
      const d = diff(e[k], a[k], `${path}.${k}`);
      if (d) return d;
    }
    for (const k of Object.keys(a)) if (!(k in e)) return `${path}.${k}: unexpected key (value ${show(a[k])})`;
    return null;
  }

  return expected === actual || kind(expected) === "null" ? null : `${path}: expected ${show(expected)}, got ${show(actual)}`;
}
