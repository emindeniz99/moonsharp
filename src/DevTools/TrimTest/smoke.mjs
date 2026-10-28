// Serves a published TrimTest folder and runs it in headless Chromium.
// Usage: node smoke.mjs <publish-dir>/wwwroot
// Exit code 0 only when the page reports the expected values.
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

const EXPECTED = { run: 'OK1,2function', libraries: 'ok' };
const root = process.argv[2];
if (!root) {
    console.error('usage: node smoke.mjs <publish-dir>/wwwroot');
    process.exit(2);
}

function loadPlaywright() {
    const attempts = [import.meta.url];
    try {
        attempts.push(join(execFileSync('npm', ['root', '-g'], { encoding: 'utf8' }).trim(), 'x.js'));
    } catch { /* npm missing: the first attempt has to work */ }
    for (const base of attempts) {
        try {
            return createRequire(base)('playwright');
        } catch { /* try the next location */ }
    }
    throw new Error('playwright is not installed (npm i -g playwright)');
}

const types = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.json': 'application/json', '.wasm': 'application/wasm', '.dll': 'application/octet-stream',
    '.dat': 'application/octet-stream', '.webcil': 'application/octet-stream', '.map': 'application/json',
};

const server = createServer(async (req, res) => {
    const path = normalize(decodeURIComponent(new URL(req.url, 'http://x').pathname)).replace(/^(\.\.[/\\])+/, '');
    const file = path === '/' ? '/index.html' : path;
    try {
        const body = await readFile(join(root, file));
        res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
        res.end(body);
    } catch {
        res.writeHead(404);
        res.end('not found');
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const url = `http://127.0.0.1:${server.address().port}/`;

const { chromium } = loadPlaywright();
const browser = await chromium.launch();
let result;
try {
    const page = await browser.newPage();
    page.on('pageerror', e => console.error('pageerror:', e.message));
    await page.goto(url);
    await page.waitForFunction(() => window.smokeResult !== undefined, null, { timeout: 60000 });
    result = await page.evaluate(() => window.smokeResult);
} catch (e) {
    result = `HARNESS FAILURE ${e.message}`;
} finally {
    await browser.close();
    server.close();
}

const failed = typeof result !== 'object' || Object.keys(EXPECTED).some(k => result[k] !== EXPECTED[k]);
console.log('result:  ', typeof result === 'object' ? JSON.stringify(result) : result);
console.log('expected:', JSON.stringify(EXPECTED));
process.exit(failed ? 1 : 0);
