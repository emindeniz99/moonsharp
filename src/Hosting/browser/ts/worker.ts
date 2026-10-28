// Runs MoonSharp inside a Web Worker so long scripts do not block the page.
// Messages: { id, method, args } -> { id, result } or { id, error }.
import { createMoonSharp, ScriptContext } from "./moonsharp.js";

const ready = createMoonSharp("../_framework");
const contexts = new Map<number, ScriptContext>();

function context(id: number): ScriptContext {
  const c = contexts.get(id);
  if (!c) throw new Error(`no script context ${id}`);
  return c;
}

const scope = self as unknown as {
  addEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
  postMessage: (message: unknown) => void;
};

// addEventListener rather than onmessage: the .NET runtime may install its own onmessage in a worker.
scope.addEventListener("message", async (e: MessageEvent) => {
  const { id, method, args } = e.data as { id: number; method: string; args: any[] };

  try {
    const runtime = await ready;
    let result: unknown;

    switch (method) {
      case "version":
        result = runtime.version;
        break;
      case "createContext": {
        const c = runtime.createContext();
        contexts.set(c.id, c);
        result = c.id;
        break;
      }
      case "dispose":
        context(args[0]).dispose();
        contexts.delete(args[0]);
        result = true;
        break;
      case "validate":
        result = context(args[0]).validate(args[1], args[2]);
        break;
      case "load":
        result = context(args[0]).load(args[1], args[2]);
        break;
      case "invoke":
        result = context(args[0]).invoke(args[1], args[2], args[3]);
        break;
      case "run":
        result = context(args[0]).run(args[1], args[2], args[3]);
        break;
      default:
        throw new Error(`unknown method ${method}`);
    }

    scope.postMessage({ id, result });
  } catch (err) {
    scope.postMessage({ id, error: String(err) });
  }
});
