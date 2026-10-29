// First-load budget check (budget.json, docs/hosting.md "Browser budget"). Loads the published page in
// headless Chromium with an empty cache several times, then reports:
// - the bytes the page fetches before the runtime is ready, counted as brotli (the .br file next to each
//   published file, which a production server sends) and raw;
// - the desktop start time: navigation to a usable runtime (window.moonsharp), median of the runs;
// - the first load the target's assumptions give for a mid-range phone on slow 4G;
// - the first paint (the page is usable for typing while the runtime downloads);
// - one cold load in Chromium under DevTools slow-4G and 4x CPU throttling, files sent as brotli: a check
//   on the model that needs no assumption about the request chain (reported only, CI machines vary).
// Exits 1 when the bytes or the start time exceed the build's entry in budget.json's "enforced". With GITHUB_STEP_SUMMARY set,
// appends a table to the job summary.
// usage: node test/budget.mjs <published wwwroot> <fixturesDir> [interpreter|aot] [runs]
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { serve } from "./serve.mjs";

const { chromium } = createRequire(import.meta.url)("playwright");
const [root, fixtures, build = "interpreter", runsArg = "5"] = process.argv.slice(2);
const budget = JSON.parse(fs.readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "budget.json"), "utf8"));
const runs = Number(runsArg);
const enforced = budget.enforced[build];
if (!enforced) throw new Error(`unknown build ${build}; budget.json has ${Object.keys(budget.enforced).join(", ")}`);

const server = await serve(root, fixtures);
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch();
const starts = [], paints = [];
let files;
const firstPaint = page => page.evaluate(() => new Promise(resolve => new PerformanceObserver(list => {
  for (const e of list.getEntries()) if (e.name === "first-contentful-paint") resolve(Math.round(e.startTime));
}).observe({ type: "paint", buffered: true })));

try {
  for (let i = 0; i < runs; i++) {
    const context = await browser.newContext();   // empty cache every run
    const page = await context.newPage();
    const fetched = new Set();
    page.on("request", r => fetched.add(new URL(r.url()).pathname));
    const t0 = Date.now();
    await page.goto(`${origin}/`);
    await page.waitForFunction(() => window.moonsharp !== undefined, null, { timeout: 60000, polling: 10 });
    starts.push(Date.now() - t0);
    paints.push(await firstPaint(page));
    files ??= [...fetched].sort();
    await context.close();
  }
} finally {
  server.close();
}

const slow4g = await emulateSlow4g().finally(() => browser.close());

async function emulateSlow4g() {
  const brotliServer = await serve(root, fixtures, 0, { brotli: true });
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    const cdp = await context.newCDPSession(page);
    await cdp.send("Network.enable");
    await cdp.send("Network.emulateNetworkConditions", {
      offline: false, latency: budget.target.rttMs,
      downloadThroughput: budget.target.throughputKbps * 1024 / 8, uploadThroughput: 750 * 1024 / 8,
    });
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: budget.target.cpuSlowdown });
    const t0 = Date.now();
    await page.goto(`http://127.0.0.1:${brotliServer.address().port}/`);
    await page.waitForFunction(() => window.moonsharp !== undefined, null, { timeout: 120000, polling: 50 });
    return { readyMs: Date.now() - t0, paintMs: await firstPaint(page) };
  } finally {
    await context.close();
    brotliServer.close();
  }
}

const size = f => (fs.existsSync(f) ? fs.statSync(f).size : 0);
let raw = 0, brotli = 0;
for (const url of files) {
  const file = path.join(root, url === "/" ? "index.html" : decodeURIComponent(url.slice(1)));
  raw += size(file);
  brotli += fs.existsSync(file + ".br") ? size(file + ".br") : size(file);
}

starts.sort((a, b) => a - b);
paints.sort((a, b) => a - b);
const startMs = starts[Math.floor(starts.length / 2)];
const paintMs = paints[Math.floor(paints.length / 2)];
const a = budget.target;
const bytesPerMs = a.throughputKbps * 1024 / 8 / 1000;
const networkMs = a.roundTrips * a.rttMs + brotli / bytesPerMs;
const phoneMs = networkMs + a.cpuSlowdown * startMs;

const failures = [];
if (brotli > enforced.brotliBytes) failures.push(`brotli bytes ${brotli} > budget ${enforced.brotliBytes}`);
if (startMs > enforced.desktopStartMs) failures.push(`desktop start ${startMs} ms > budget ${enforced.desktopStartMs} ms`);

const rows = [
  ["Files fetched before the runtime is ready", `${files.length}`, ""],
  ["Bytes, brotli", `${brotli.toLocaleString("en-US")}`, `${enforced.brotliBytes.toLocaleString("en-US")}`],
  ["Bytes, raw", `${raw.toLocaleString("en-US")}`, ""],
  [`Desktop start, headless Chromium ${browser.version()}, median of ${runs} (${starts.join(", ")} ms)`, `${startMs} ms`, `${enforced.desktopStartMs} ms`],
  [`Desktop first paint, median of ${runs}`, `${paintMs} ms`, ""],
  [`Mid-range phone on slow 4G: ${a.roundTrips} x ${a.rttMs} ms + bytes at ${a.throughputKbps} kbit/s + ${a.cpuSlowdown} x start`,
    `${(phoneMs / 1000).toFixed(1)} s`, `target ${a.firstLoadMs / 1000} s, not enforced (budget.json)`],
  [`Chromium emulating slow 4G and a ${a.cpuSlowdown}x slower CPU, one load, without DNS, TCP and TLS: runtime ready / first paint`,
    `${(slow4g.readyMs / 1000).toFixed(1)} s / ${(slow4g.paintMs / 1000).toFixed(1)} s`, "reported only"],
];
const table = ["| Measurement | Now | Budget |", "|---|---|---|", ...rows.map(r => `| ${r.join(" | ")} |`)].join("\n");

console.log(table);
console.log(JSON.stringify({ build, files, brotliBytes: brotli, rawBytes: raw, startsMs: starts, paintsMs: paints, phoneMs: Math.round(phoneMs),
  slow4gReadyMs: slow4g.readyMs, slow4gPaintMs: slow4g.paintMs }));
if (process.env.GITHUB_STEP_SUMMARY)
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Browser first-load budget, ${build} build\n\n${table}\n\n${failures.length ? "Over budget: " + failures.join("; ") : "Within budget."}\n`);

for (const f of failures) console.log(`OVER BUDGET: ${f}`);
console.log(failures.length === 0 ? `browser budget (${build}): within budget` : `browser budget (${build}): over budget`);
process.exit(failures.length === 0 ? 0 : 1);
