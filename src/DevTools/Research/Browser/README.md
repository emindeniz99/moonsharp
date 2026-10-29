# Browser build checks

Two Playwright scripts behind [docs/hosting.md](../../../../docs/hosting.md), "Two browser builds":

| File | What it does |
|---|---|
| `speed.mjs` | The "Script speed" table: `fib_recursive`, `string_pattern` and `table_hash_set_get` (sources from `src/DevTools/Benchmarks/LuaWorkloads.cs`, smaller counts) through `createContext().run()`, and the cached `eligibility` invoke with JSON arguments; one warm-up, then the median, minimum and maximum of N runs, as one JSON line |
| `cross.mjs` | The "To switch" check: a page served from the interpreter build loads the AOT build's runtime with `createMoonSharp("./aot/_framework")` on the main thread and `WorkerClient.start("./aot/js/worker.js")` in a Web Worker, and prints which `dotnet.native.wasm` each fetched |

Both use the browser package's Playwright and its `test/serve.mjs`, so they run against
`src/Hosting/browser` after `npm ci`. Nothing here is built or installed by the repository's projects.

## Inputs

The two browser builds, published as in [AGENTS.md](../../../../AGENTS.md) ("Browser") and docs/hosting.md:
the interpreter build's `bin/Release/net10.0/publish/wwwroot` and the AOT build's. Copy each to its own
folder before publishing the other, since both publish to the same path.

## Run

From the repository root, after `cd src/Hosting/browser && npm ci && npx playwright install chromium`:

```sh
b=src/Hosting/browser
# Speed: one line per build and browser
mise exec -- node src/DevTools/Research/Browser/speed.mjs $b /tmp/www-interp src/Hosting/fixtures chromium 5
mise exec -- node src/DevTools/Research/Browser/speed.mjs $b /tmp/www-aot src/Hosting/fixtures chromium 5

# Cross-build loading: the interpreter page with the AOT build under aot/
cp -r /tmp/www-interp /tmp/www-cross && cp -r /tmp/www-aot /tmp/www-cross/aot && : > /tmp/www-cross/blank.html
mise exec -- node src/DevTools/Research/Browser/cross.mjs $b /tmp/www-cross src/Hosting/fixtures
```

Both print their usage when called without arguments. Run one browser process at a time, with a timeout.
