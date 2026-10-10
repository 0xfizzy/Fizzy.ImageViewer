import { chromium } from '@playwright/test';
import { createServer } from 'node:http';
import { readFile, stat, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const repo = path.resolve(import.meta.dirname, '..');
const site = path.join(repo, 'artifacts/docs/site');
const urlIndex = process.argv.indexOf('--url');
const liveUrl = urlIndex >= 0 ? new URL(process.argv[urlIndex + 1]).href.replace(/\/?$/, '/') : null;
const identity = liveUrl ? await fetch(new URL('identity.json', liveUrl)).then(r => { assert(r.ok); return r.json(); }) : JSON.parse(await readFile(path.join(site, 'identity.json'), 'utf8'));
const mounts = ['v1.2.0/r0', 'v1.2.1/r1', 'dev'];
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const server = createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (pathname === `${identity.basePath}versions.json`) {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ development: { commit: identity.commit }, versions: ['1.2.1','1.2.0'].map(version => ({ version, completed: true, revisions: [{}], currentRevision: version === '1.2.1' ? 1 : 0 })) })); return;
    }
    const directory = mounts.find(v => pathname.startsWith(`${identity.basePath}${v}/`));
    if (!directory) { res.writeHead(404).end(); return; }
    const relative = pathname.slice(`${identity.basePath}${directory}/`.length) || 'index.html';
    const target = path.resolve(site, relative);
    if (!target.startsWith(site + path.sep)) { res.writeHead(403).end(); return; }
    // Simulate a page removed in the older version to exercise language-preserving fallback.
    if (directory === 'v1.2.0/r0' && relative === 'docs/zh-CN/snapshots.html') { res.writeHead(404).end(); return; }
    if (relative === 'identity.json') {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ ...identity, version: directory.slice(1).split('/')[0], directory, channel: directory === 'dev' ? 'Development' : 'Release' })); return;
    }
    const file = (await stat(target)).isDirectory() ? path.join(target, 'index.html') : target;
    res.setHeader('content-type', mime[path.extname(file)] || 'application/octet-stream');
    res.end(await readFile(file));
  } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const base = liveUrl || `${origin}${identity.basePath}v1.2.1/r1/`;
let browser;
let page;
const output = path.join(repo, 'artifacts/docs/browser');
try {
  browser = await chromium.launch({ headless: true });
  page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  for (const relative of ['docs/index.html', 'docs/zh-CN/index.html', 'api/Fizzy.ImageViewer.Frames.FrameLease.html']) {
    const response = await page.goto(base + relative);
    assert.equal(response.status(), 200, relative);
    await page.locator('#documentation-version').waitFor();
    assert.match(await page.locator('#documentation-version').innerText(), liveUrl ? /Development \(unreleased\)/ : /v1\.2\.1/);
    const broken = await page.locator('img').evaluateAll(images => images.filter(i => !i.complete || i.naturalWidth === 0).map(i => i.src));
    assert.deepEqual(broken, [], relative);
  }
  if (!liveUrl) {
    await page.getByLabel('Documentation version').selectOption('v1.2.0/r0');
    await page.waitForURL('**/v1.2.0/r0/api/Fizzy.ImageViewer.Frames.FrameLease.html');
    await page.goto(base + 'docs/zh-CN/snapshots.html');
    await page.getByLabel('Documentation version').selectOption('v1.2.0/r0');
    await page.waitForURL('**/v1.2.0/r0/docs/zh-CN/index.html');
  }
  await page.goto(base + 'docs/index.html');
  await page.getByRole('link', { name: '简体中文', exact: true }).click();
  await page.waitForURL('**/docs/zh-CN/index.html');
  await page.getByRole('link', { name: 'English', exact: true }).click();
  await page.waitForURL('**/docs/index.html');
  await page.goto(base + 'index.html');
  const search = page.locator('input[type="search"], #search-query').first();
  await search.fill('FrameLease');
  await page.waitForFunction(() => [...document.querySelectorAll('a')].some(a => a.href.includes('FrameLease') && a.getClientRects().length > 0));
  const results = await page.locator('a').evaluateAll(links => links.filter(a => a.href.includes('FrameLease') && a.getClientRects().length > 0).map(a => a.href));
  assert(results.some(href => href.startsWith(base + 'api/')));
  await page.goto(results.find(href => href.startsWith(base + 'api/')));
  await page.locator('#documentation-version').waitFor();
  assert.deepEqual(errors, []);
  await mkdir(output, { recursive: true });
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ passed: true, chromium: browser.version(), sourceCommit: identity.commit }));
  console.log('PASS: project path, nested API, Chinese navigation, search, version switching and missing-page fallback.');
} catch (error) {
  await mkdir(output, { recursive: true });
  await page?.screenshot({ path: path.join(output, 'failure.png'), fullPage: true });
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ passed: false, error: String(error), sourceCommit: identity.commit }));
  throw error;
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
