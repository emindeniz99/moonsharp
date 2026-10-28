// Headless browser smoke test: in each browser, every fixture suite runs in the page and is checked
// against known-failures/browser-<browser>.json, and an eligibility call runs through a Web Worker.
// usage: node test/smoke.mjs <published wwwroot> <fixturesDir> [chromium] [firefox] [webkit]
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { serve } from "./serve.mjs";

// require() also finds a globally installed Playwright, which ESM import does not.
const playwright = createRequire(import.meta.url)("playwright");

const [root, fixtures, ...names] = process.argv.slice(2);
const server = await serve(root, fixtures);
const origin = `http://127.0.0.1:${server.address().port}`;
let failed = 0;

try {
  for (const name of names.length > 0 ? names : ["chromium"]) {
    if (!["chromium", "firefox", "webkit"].includes(name)) throw new Error(`unknown browser ${name}`);
    const browser = await playwright[name].launch();
    try {
      await smoke(browser, `browser-${name}`);
    } finally {
      await browser.close();
    }
  }
} finally {
  server.close();
}

console.log(failed === 0 ? "browser smoke: all passed" : `browser smoke: ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);

async function smoke(browser, host) {
  const page = await browser.newPage();
  const transferred = [];
  page.on("response", async r => {
    try { transferred.push({ url: r.url(), bytes: (await r.body()).length }); } catch { }
  });
  page.on("pageerror", e => { console.log(`${host}: page error: ${e}`); failed++; });

  const t0 = Date.now();
  await page.goto(`${origin}/`);
  await page.waitForFunction(() => window.moonsharp !== undefined, null, { timeout: 60000 });
  const startupMs = Date.now() - t0;

  const suiteFiles = fs.readdirSync(fixtures).filter(f => f.endsWith(".suite.json")).sort();
  const report = await page.evaluate(async files => {
    const { runtime, runSuite } = window.moonsharp;
    const suites = [];
    for (const f of files) suites.push(await runSuite(runtime, `./fixtures/${f}`));
    return { version: runtime.version, suites };
  }, suiteFiles);

  failed += checkKnownFailures(report.suites, host, path.join(fixtures, "known-failures", `${host}.json`));

  // The same eligibility call through the Web Worker.
  const viaWorker = await page.evaluate(async () => {
    const { WorkerClient } = await import("./js/worker-client.js");
    const w = WorkerClient.start("./js/worker.js");
    const ctx = await w.createContext();
    const src = await (await fetch("./fixtures/scripts/eligibility.lua")).text();
    await w.load(ctx, "eligibility", src);
    const r = await w.invoke(ctx, "eligibility", "eligible", [{ level: 12, country: "DE", flags: { beta: true } }, "offers"]);
    const big = await w.invoke(ctx, "eligibility", "eligible", { json: "[9007199254740993]" });
    const limited = await w.createContext({ instructionLimit: 100000 });
    const loop = await w.run(limited, "loop.lua", "while true do end");
    w.terminate();
    return { r, big, loop };
  });

  const workerOk = viaWorker.r.ok && viaWorker.r.value === true && !viaWorker.big.ok && viaWorker.big.error.kind === "input"
    && !viaWorker.loop.ok && viaWorker.loop.error.kind === "interrupted";
  console.log(`${workerOk ? "pass" : "FAIL"}  ${host} web worker / eligibility, 2^53+1 rejection, instruction limit`);
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
  console.log(JSON.stringify({ host, engine: report.version, browser: browser.version(), startupMs, transferredBytes: bytes,
    frameworkBytes: framework, loadEligibilityMs: +perf.loadMs.toFixed(2), invokeUs: +perf.perCallUs.toFixed(2) }));
  await page.close();
}

/**
 * The same two-way check as the .NET runner (KnownFailures in FixtureRunner.cs): a new failure, a changed
 * detail, a listed failure that passes now and a listed case that no longer exists each count as a failure.
 */
function checkKnownFailures(suites, host, file) {
  const known = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : {};
  const actual = {}, ran = new Set(), seen = new Set();
  const n = { cases: 0, passed: 0, known: 0, new: 0, changed: 0, fixed: 0, stale: 0 };

  for (const s of suites) {
    ran.add(s.suite);
    for (const c of s.cases) {
      const key = `${s.suite} / ${c.name}`, listed = Object.hasOwn(known, key);
      seen.add(key);
      n.cases++;
      if (!c.pass) actual[key] = c.detail;
      if (c.pass && !listed) { n.passed++; console.log(`pass  ${key}`); }
      else if (c.pass) { n.fixed++; console.log(`FIXED ${key}: passes now, remove it from ${path.basename(file)}`); }
      else if (!listed) { n.new++; console.log(`FAIL  ${key}: ${c.detail}`); }
      else if (known[key] !== c.detail) { n.changed++; console.log(`CHANGED ${key}: ${c.detail} (known: ${known[key]})`); }
      else { n.known++; console.log(`known ${key}: ${c.detail}`); }
    }
  }

  for (const key of Object.keys(known)) {
    const suite = key.split(" / ")[0];
    if (ran.has(suite) && !seen.has(key)) { n.stale++; console.log(`STALE ${key}: listed in ${path.basename(file)}, but there is no such case`); }
    else if (!ran.has(suite)) actual[key] = known[key];
  }

  console.log(`${host}: ${n.cases} cases, ${n.passed} passed, ${n.known} known failures; ${n.new} new, ${n.changed} changed, ${n.fixed} fixed, ${n.stale} stale`);
  const errors = n.new + n.changed + n.fixed + n.stale;
  if (errors > 0) {
    const sorted = Object.fromEntries(Object.keys(actual).sort().map(k => [k, actual[k]]));
    console.log(`The known failures changed. If that is intended, write this to ${file}:\n${JSON.stringify(sorted, null, 2)}`);
  }
  return errors;
}
