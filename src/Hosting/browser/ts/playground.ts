import { createMoonSharp, HostResult, MoonSharpRuntime, ScriptContext } from "./moonsharp.js";
import { runSuite } from "./fixtures.js";
import { WorkerClient } from "./worker-client.js";

const samples: Record<string, { fn: string; args: string; source: string }> = {
  "eligibility v1": {
    fn: "eligible",
    args: '[{ "level": 12, "country": "DE", "flags": { "beta": true } }, "offers"]',
    source: `local M = {}

function M.eligible(player, feature)
  print("checking " .. feature .. " for level " .. player.level)
  if player.level < 10 then return false end
  if feature == "offers" then return player.flags.beta == true end
  return true
end

return M`,
  },
  "eligibility v2 (stricter)": {
    fn: "eligible",
    args: '[{ "level": 12, "country": "DE", "flags": { "beta": true } }, "offers"]',
    source: `local M = {}

function M.eligible(player, feature)
  if player.level < 20 then return false end
  return feature ~= "offers" or player.flags.beta == true
end

return M`,
  },
  "json document": {
    fn: "build",
    args: '[{ "id": "18446744073709551615", "tags": ["a", "b"], "items": [] }]',
    source: `local M = {}

function M.build(input)
  return {
    id = input.id,
    tags = input.tags,
    items = input.items,          -- an empty input array stays []
    none = json.emptyarray(),
    ratio = 0.1 + 0.2,
    text = "Ünïcödé ✓",
  }
end

return M`,
  },
  "runtime error": {
    fn: "fail",
    args: '["boom"]',
    source: `local M = {}

function M.fail(reason)
  local t = nil
  return t.field .. reason      -- line 5: indexes nil
end

return M`,
  },
};

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const status = $("status"), result = $("result"), output = $("output");

let runtime: MoonSharpRuntime;
let context: ScriptContext;
let worker: WorkerClient | null = null;
let workerContext = 0;

function show(r: HostResult, label: string) {
  output.textContent = r.output;
  if (r.ok) {
    const t = r.timing.compileMs ?? r.timing.runMs;
    status.className = "status ok";
    status.textContent = `${label}: ok${t !== undefined ? ` in ${t} ms` : ""}`;
    result.textContent = JSON.stringify(r.value, null, 2);
  } else {
    status.className = "status bad";
    const where = r.error.line !== null ? ` at ${r.error.chunk}:${r.error.line}` : "";
    status.textContent = `${label}: ${r.error.kind} error${where}`;
    result.textContent = r.error.message + (r.error.stack.length ? "\n\n" + r.error.stack.join("\n") : "");
  }
}

const moduleId = () => $<HTMLInputElement>("module").value;
const fnName = () => $<HTMLInputElement>("function").value;
const source = () => $<HTMLTextAreaElement>("source").value;

function args(): { json: string } {
  const text = $<HTMLTextAreaElement>("args").value.trim();
  return { json: text === "" ? "[]" : text };   // passed as text, so large integers are not rounded by JSON.parse
}

// A script typed here that never returns stops after 5 s instead of freezing the page.
const limits = { timeLimitMs: 5000 };

async function useWorker(): Promise<boolean> {
  if (!$<HTMLInputElement>("use-worker").checked) return false;
  if (!worker) {
    worker = WorkerClient.start();
    workerContext = await worker.createContext(limits);
  }
  return true;
}

async function load() {
  const r = (await useWorker())
    ? await worker!.load(workerContext, moduleId(), source())
    : context.load(moduleId(), source());
  show(r, "load");
  return r.ok;
}

$("validate").onclick = async () => {
  show((await useWorker()) ? await worker!.validate(workerContext, moduleId(), source()) : context.validate(moduleId(), source()), "validate");
};

$("load").onclick = load;

$("invoke").onclick = async () => {
  if (!(await load())) return;
  const r = (await useWorker())
    ? await worker!.invoke(workerContext, moduleId(), fnName(), args())
    : context.invoke(moduleId(), fnName(), args());
  show(r, "invoke");
};

$("bench").onclick = async () => {
  if (!(await load())) return;
  const n = 1000, a = args(), inWorker = await useWorker();
  const start = performance.now();
  let last: HostResult | undefined;
  for (let i = 0; i < n; i++)
    last = inWorker ? await worker!.invoke(workerContext, moduleId(), fnName(), a) : context.invoke(moduleId(), fnName(), a);
  const perCall = (performance.now() - start) / n;
  show(last!, "invoke");
  status.textContent += ` · ${n} calls, ${(perCall * 1000).toFixed(1)} µs per call including JSON marshaling`;
};

$("fixtures").onclick = async () => {
  const report = await runSuite(runtime, "./fixtures/logic.suite.json");
  const failed = report.cases.filter(c => !c.pass).length;
  const rows = report.cases.map(c =>
    `<tr><td class="${c.pass ? "pass" : "fail"}">${c.pass ? "pass" : "FAIL"}</td><td>${escape(c.name)}${c.detail ? "<br>" + escape(c.detail) : ""}</td></tr>`);
  $("fixture-report").innerHTML = `<p>${report.cases.length - failed} passed, ${failed} failed</p><table>${rows.join("")}</table>`;
};

function escape(s: string) {
  return s.replace(/[&<>"]/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]!));
}

function selectSample(name: string) {
  const s = samples[name];
  $<HTMLTextAreaElement>("source").value = s.source;
  $<HTMLTextAreaElement>("args").value = s.args;
  $<HTMLInputElement>("function").value = s.fn;
}

async function main() {
  const select = $<HTMLSelectElement>("sample");
  for (const name of Object.keys(samples)) select.add(new Option(name, name));
  select.onchange = () => selectSample(select.value);
  selectSample(select.value);

  const start = performance.now();
  runtime = await createMoonSharp("./_framework");
  context = runtime.createContext(limits);
  $("engine").textContent = runtime.version;
  // The page is drawn and editable while the runtime downloads; the buttons need it.
  document.querySelectorAll("button").forEach(button => { button.disabled = false; });
  status.className = "status ok";
  status.textContent = `Runtime ready in ${(performance.now() - start).toFixed(0)} ms`;
  (window as any).moonsharp = { runtime, runSuite };   // used by the Playwright smoke test
}

main().catch(err => {
  status.className = "status bad";
  status.textContent = `Runtime failed to start: ${err}`;
});
