// Promise-based client for worker.ts. Same operations as ScriptContext, but asynchronous.
import type { Args, HostResult } from "./moonsharp.js";

export class WorkerClient {
  private next = 1;
  private pending = new Map<number, { resolve: (v: any) => void; reject: (e: Error) => void }>();

  constructor(private readonly worker: Worker) {
    worker.onmessage = (e: MessageEvent) => {
      const { id, result, error } = e.data;
      const p = this.pending.get(id);
      if (!p) return;
      this.pending.delete(id);
      if (error !== undefined) p.reject(new Error(error));
      else p.resolve(result);
    };
  }

  static start(workerUrl = "./js/worker.js"): WorkerClient {
    return new WorkerClient(new Worker(workerUrl, { type: "module" }));
  }

  private call<T>(method: string, ...args: unknown[]): Promise<T> {
    const id = this.next++;
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.worker.postMessage({ id, method, args });
    });
  }

  version(): Promise<string> { return this.call("version"); }
  createContext(): Promise<number> { return this.call("createContext"); }
  dispose(context: number): Promise<boolean> { return this.call("dispose", context); }
  validate(context: number, chunk: string, source: string): Promise<HostResult<null>> { return this.call("validate", context, chunk, source); }
  load<T = unknown>(context: number, moduleId: string, source: string): Promise<HostResult<T>> { return this.call("load", context, moduleId, source); }
  invoke<T = unknown>(context: number, moduleId: string, fn: string, args: Args = []): Promise<HostResult<T>> { return this.call("invoke", context, moduleId, fn, args); }
  run<T = unknown>(context: number, chunk: string, source: string, input?: unknown): Promise<HostResult<T>> { return this.call("run", context, chunk, source, input); }
  terminate(): void { this.worker.terminate(); }
}
