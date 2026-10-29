// Lua workload speed and the cached JSON invoke, per build and browser (docs/hosting.md, "Two browser builds").
// usage: node speed.mjs <src/Hosting/browser> <published wwwroot> <fixtures> [chromium|firefox|webkit] [rounds]
import { createRequire } from "node:module";
import path from "node:path";
import { pathToFileURL } from "node:url";
const [dir, root, fixtures, name = "chromium", roundsArg = "5"] = process.argv.slice(2);
if (process.argv.length < 5) { console.error("usage: node speed.mjs <src/Hosting/browser> <published wwwroot> <fixtures> [chromium|firefox|webkit] [rounds]"); process.exit(2); }
const req = createRequire(path.resolve(dir, "package.json"));
const playwright = req("playwright");
const { serve } = await import(pathToFileURL(path.resolve(dir, "test/serve.mjs")).href);
// Sources copied from src/DevTools/Benchmarks/LuaWorkloads.cs; counts smaller, as in docs/hosting.md.
const W = [
  ["fib_recursive", 2000, "local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nreturn function(n) local s = 0 for i = 1, n do s = s + fib(12) end return s end"],
  ["string_pattern", 10000, "local s = 'the quick brown fox 123 jumps over the lazy dog 4567'\nreturn function(n) local c = 0 for i = 1, n do local a, b = s:find('(%a+) (%d+)') local r, k = s:gsub('o', '0') c = c + a + k + #s:match('%d+$') end return c end"],
  ["table_hash_set_get", 100000, "local keys = {} for i = 1, 64 do keys[i] = 'key' .. i end\nreturn function(n) local t, s, j = {}, 0, 1 for i = 1, n do local k = keys[j] t[k] = i s = s + t[k] j = j + 1 if j > 64 then j = 1 end end return s end"],
];
const server = await serve(root, fixtures);
const browser = await playwright[name].launch();
try {
  const page = await (await browser.newContext()).newPage();
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.waitForFunction(() => window.moonsharp !== undefined, null, { timeout: 60000 });
  const out = await page.evaluate(async ({ W, rounds }) => {
    const { runtime } = window.moonsharp;
    const res = {};
    for (const [name, n, src] of W) {
      const ctx = runtime.createContext();
      const once = () => { const t = performance.now(); const r = ctx.run(name, src, JSON.stringify(n)); if (!r.ok) throw new Error(JSON.stringify(r.error)); return performance.now() - t; };
      once();
      const ts = []; for (let i = 0; i < rounds; i++) ts.push(once());
      ts.sort((a, b) => a - b); res[name] = { median: +ts[Math.floor(ts.length / 2)].toFixed(1), min: +ts[0].toFixed(1), max: +ts[ts.length - 1].toFixed(1) };
      ctx.dispose();
    }
    // JSON invoke, as smoke.mjs measures it
    const ctx = runtime.createContext();
    const src = await (await fetch("./fixtures/scripts/eligibility.lua")).text();
    ctx.load("eligibility", src);
    const args = [{ level: 12, country: "DE", flags: { beta: true } }, "offers"];
    for (let i = 0; i < 2000; i++) ctx.invoke("eligibility", "eligible", args);
    const us = [];
    for (let r = 0; r < rounds; r++) { const t = performance.now(); for (let i = 0; i < 20000; i++) ctx.invoke("eligibility", "eligible", args); us.push((performance.now() - t) * 1000 / 20000); }
    us.sort((a, b) => a - b); res.invoke_json_us = { median: +us[Math.floor(us.length / 2)].toFixed(2), min: +us[0].toFixed(2), max: +us[us.length - 1].toFixed(2) };
    return res;
  }, { W, rounds: Number(roundsArg) });
  console.log(JSON.stringify({ browser: name, version: browser.version(), root, ...out }));
} finally { await browser.close(); server.close(); }
