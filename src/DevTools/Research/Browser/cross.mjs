// A page served from the interpreter build loads the AOT build's runtime by URL, on the main thread and in a
// Web Worker (docs/hosting.md, "Two browser builds"). usage: node cross.mjs <src/Hosting/browser> <root> <fixtures>
// <root>: the interpreter build's wwwroot with the AOT build's wwwroot copied into aot/ and an empty blank.html.
import { createRequire } from "node:module";
import path from "node:path";
import { pathToFileURL } from "node:url";
const [dir, root, fixtures] = process.argv.slice(2);
if (process.argv.length < 5) { console.error("usage: node cross.mjs <src/Hosting/browser> <root> <fixtures>"); process.exit(2); }
const { chromium } = createRequire(path.resolve(dir, "package.json"))("playwright");
const { serve } = await import(pathToFileURL(path.resolve(dir, "test/serve.mjs")).href);
const server = await serve(root, fixtures);
const browser = await chromium.launch();
try {
  for (const [label, fw, worker] of [["main thread", "./aot/_framework", null], ["worker", null, "./aot/js/worker.js"]]) {
    const page = await (await browser.newContext()).newPage();
    const urls = [];
    page.on("request", r => urls.push(new URL(r.url()).pathname));
    await page.goto(`http://127.0.0.1:${server.address().port}/blank.html`);
    const r = await page.evaluate(async ({ fw, worker }) => {
      if (fw) {
        const { createMoonSharp } = await import("./js/moonsharp.js");
        const rt = await createMoonSharp(fw);
        return rt.createContext().run("x.lua", "return 6 * 7");
      }
      const { WorkerClient } = await import("./js/worker-client.js");
      const w = WorkerClient.start(worker);
      const ctx = await w.createContext();
      const res = await w.run(ctx, "x.lua", "return 6 * 7");
      w.terminate();
      return res;
    }, { fw, worker });
    const wasm = urls.filter(u => u.includes("dotnet.native") && u.endsWith(".wasm"));
    console.log(label, JSON.stringify(r.ok ? r.value : r), wasm);
  }
} finally { await browser.close(); server.close(); }
