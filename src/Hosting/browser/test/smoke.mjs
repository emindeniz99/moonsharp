// Headless browser smoke test: the shared fixtures must pass in the page and in a Web Worker.
// usage: node test/smoke.mjs <published wwwroot> <fixturesDir>
import { createRequire } from "node:module";
import { serve } from "./serve.mjs";

// require() also finds a globally installed Playwright, which ESM import does not.
const { chromium } = createRequire(import.meta.url)("playwright");

const [root, fixtures] = process.argv.slice(2);
const server = await serve(root, fixtures);
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch();
let failed = 0;

try {
  const page = await browser.newPage();
  const transferred = [];
  page.on("response", async r => {
    try { transferred.push({ url: r.url(), bytes: (await r.body()).length }); } catch { }
  });
  page.on("pageerror", e => { console.log(`page error: ${e}`); failed++; });

  const t0 = Date.now();
  await page.goto(`${origin}/`);
  await page.waitForFunction(() => window.moonsharp !== undefined, null, { timeout: 60000 });
  const startupMs = Date.now() - t0;

  const report = await page.evaluate(async () => {
    const { runtime, runSuite } = window.moonsharp;
    return { version: runtime.version, suite: await runSuite(runtime, "./fixtures/logic.suite.json") };
  });

  for (const c of report.suite.cases) {
    console.log(`${c.pass ? "pass" : "FAIL"}  main thread / ${c.name}${c.pass ? "" : ": " + c.detail}`);
    if (!c.pass) failed++;
  }

  // The same eligibility call through the Web Worker.
  const viaWorker = await page.evaluate(async () => {
    const { WorkerClient } = await import("./js/worker-client.js");
    const w = WorkerClient.start("./js/worker.js");
    const ctx = await w.createContext();
    const src = await (await fetch("./fixtures/scripts/eligibility.lua")).text();
    await w.load(ctx, "eligibility", src);
    const r = await w.invoke(ctx, "eligibility", "eligible", [{ level: 12, country: "DE", flags: { beta: true } }, "offers"]);
    const big = await w.invoke(ctx, "eligibility", "eligible", { json: "[9007199254740993]" });
    w.terminate();
    return { r, big };
  });

  const workerOk = viaWorker.r.ok && viaWorker.r.value === true && !viaWorker.big.ok && viaWorker.big.error.kind === "input";
  console.log(`${workerOk ? "pass" : "FAIL"}  web worker / eligibility and 2^53+1 rejection`);
  if (!workerOk) { failed++; console.log(JSON.stringify(viaWorker)); }

  // Cached invocation cost, including JSON marshaling between JS and .NET.
  const perf = await page.evaluate(async () => {
    const { runtime } = window.moonsharp;
    const ctx = runtime.createContext();
    const src = await (await fetch("./fixtures/scripts/eligibility.lua")).text();
    const t0 = performance.now();
    ctx.load("eligibility", src);
    const loadMs = performance.now() - t0;
    const args = [{ level: 12, country: "DE", flags: { beta: true } }, "offers"];
    for (let i = 0; i < 2000; i++) ctx.invoke("eligibility", "eligible", args);
    const n = 20000, t1 = performance.now();
    for (let i = 0; i < n; i++) ctx.invoke("eligibility", "eligible", args);
    const perCallUs = (performance.now() - t1) * 1000 / n;
    ctx.dispose();
    return { loadMs, perCallUs };
  });

  const bytes = transferred.reduce((s, t) => s + t.bytes, 0);
  const framework = transferred.filter(t => t.url.includes("/_framework/")).reduce((s, t) => s + t.bytes, 0);
  console.log(JSON.stringify({ engine: report.version, browser: browser.version(), startupMs, transferredBytes: bytes,
    frameworkBytes: framework, loadEligibilityMs: +perf.loadMs.toFixed(2), invokeUs: +perf.perCallUs.toFixed(2) }));
} finally {
  await browser.close();
  server.close();
}

console.log(failed === 0 ? "browser smoke: all passed" : `browser smoke: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
