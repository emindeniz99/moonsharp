// Minimal static server for the playground: serves the published wwwroot and the shared fixtures.
// usage: node test/serve.mjs <wwwroot> <fixturesDir> [port]
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const types = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".mjs": "text/javascript",
  ".css": "text/css", ".json": "application/json", ".wasm": "application/wasm",
  ".lua": "text/plain; charset=utf-8", ".dat": "application/octet-stream", ".map": "application/json",
};

export function serve(root, fixtures, port = 0) {
  const server = http.createServer((req, res) => {
    const url = decodeURIComponent(new URL(req.url, "http://x").pathname);
    const [base, rel] = url.startsWith("/fixtures/") ? [fixtures, url.slice("/fixtures/".length)] : [root, url.slice(1) || "index.html"];
    const file = path.resolve(base, rel);

    if (!file.startsWith(path.resolve(base)) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      res.writeHead(404).end("not found");
      return;
    }

    res.writeHead(200, { "content-type": types[path.extname(file)] ?? "application/octet-stream" });
    fs.createReadStream(file).pipe(res);
  });

  return new Promise(resolve => server.listen(port, "127.0.0.1", () => resolve(server)));
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const [root, fixtures, port = "8080"] = process.argv.slice(2);
  const server = await serve(root, fixtures, Number(port));
  console.log(`serving ${root} on http://127.0.0.1:${server.address().port}/`);
}
