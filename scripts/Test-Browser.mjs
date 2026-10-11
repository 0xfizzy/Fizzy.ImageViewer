import { chromium, expect } from '@playwright/test';
import { createServer } from 'node:http';
import { readFile, stat, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const repo = process.env.DOCS_SOURCE_ROOT || path.resolve(import.meta.dirname, '..');
const siteIndex = process.argv.indexOf('--site');
const site = siteIndex >= 0 ? path.resolve(process.argv[siteIndex + 1]) : path.join(repo, 'artifacts/docs/site');
const urlIndex = process.argv.indexOf('--url');
const liveUrl = urlIndex >= 0 ? new URL(process.argv[urlIndex + 1]).href.replace(/\/?$/, '/') : null;
const identity = liveUrl ? await fetch(new URL('identity.json', liveUrl)).then(r => { assert(r.ok); return r.json(); }) : JSON.parse(await readFile(path.join(site, 'identity.json'), 'utf8'));
const mounts = ['v1.2.0', 'v1.2.1', 'dev'];
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const server = createServer(async (req, res) => {
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (pathname === `${identity.basePath}versions.json`) {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ development: { commit: identity.commit }, versions: ['1.2.1','1.2.0'].map(version => ({ version, completed: true })) })); return;
    }
    const directory = mounts.find(v => pathname.startsWith(`${identity.basePath}${v}/`));
    if (!directory) { res.writeHead(404).end(); return; }
    const relative = pathname.slice(`${identity.basePath}${directory}/`.length) || 'index.html';
    const target = path.resolve(site, relative);
    if (!target.startsWith(site + path.sep)) { res.writeHead(403).end(); return; }
    // Simulate a page removed in the older version to exercise language-preserving fallback.
    if (directory === 'v1.2.0' && ['docs/zh-CN/snapshots.html', 'docs/en/snapshots.html', 'docs/en/live-streaming.html'].includes(relative)) { res.writeHead(404).end(); return; }
    if (relative === 'pages.json' && directory === 'v1.2.0') {
      const pages = JSON.parse(await readFile(path.join(site, 'pages.json'), 'utf8'));
      for (const page of ['docs/zh-CN/snapshots.html', 'docs/en/snapshots.html', 'docs/en/live-streaming.html', 'api/Fizzy.ImageViewer.Frames.FrameLease.html']) delete pages[page];
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify(pages)); return;
    }
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
const base = liveUrl || `${origin}${identity.basePath}v1.2.1/`;
let browser;
let page;
const output = path.join(repo, 'artifacts/docs/browser');
try {
  browser = await chromium.launch({ headless: true });
  page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  for (const relative of ['index.html', 'docs/en/index.html', 'docs/zh-CN/index.html', 'docs/en/live-streaming.html', 'docs/zh-CN/measurement-tools.html', 'api/Fizzy.ImageViewer.Frames.FrameLease.html']) {
    const response = await page.goto(base + relative);
    assert.equal(response.status(), 200, relative);
    await page.locator('#documentation-version').waitFor();
    const expectedLabel = liveUrl ? (identity.channel === 'Development' ? 'Development (unreleased)' : `v${identity.version}`) : 'v1.2.1';
    assert((await page.locator('#documentation-version > span').innerText()).startsWith(expectedLabel));
    const broken = await page.locator('img').evaluateAll(images => images.filter(i => !i.complete || i.naturalWidth === 0).map(i => i.src));
    assert.deepEqual(broken, [], relative);
  }
  if (!liveUrl) {
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/api/index.html');
    await page.goto(base + 'docs/zh-CN/snapshots.html');
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/zh-CN/index.html');
    await page.goto(base + 'docs/en/snapshots.html');
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/en/index.html');
    await page.goto(base + 'docs/en/measurements.html#queries-and-retained-results');
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/en/measurements.html#queries-and-retained-results');
    await page.getByLabel('Documentation version').selectOption('v1.2.1');
    await page.waitForURL('**/v1.2.1/docs/en/measurements.html#queries-and-retained-results');
    await page.goto(base + 'docs/en/live-streaming.html');
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/en/index.html');
    await page.goto(base + 'docs/en/index.html');
    const anchor = await page.locator('article h1').getAttribute('id');
    await page.goto(base + 'docs/en/index.html#' + anchor);
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/en/index.html#' + anchor);
    await page.goto(base + 'docs/en/index.html#removed-anchor');
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await page.waitForURL('**/v1.2.0/docs/en/index.html');
    await page.goto(base + 'docs/en/index.html');
    await page.route('**/v1.2.0/pages.json', route => route.fulfill({ status: 404 }));
    await page.getByLabel('Documentation version').selectOption('v1.2.0');
    await expect(page.locator('#documentation-version')).toHaveAttribute('title', /Page inventory unavailable/);
    await expect(page.getByLabel('Documentation version')).toHaveValue('v1.2.1');
    await expect(page.getByLabel('Documentation version')).toBeEnabled();
    await page.unroute('**/v1.2.0/pages.json');
    await page.route('**/versions.json', route => route.fulfill({ status: 404 }));
    await page.goto(base + 'docs/en/index.html');
    await expect(page.locator('#documentation-version')).toHaveAttribute('title', /No version index/);
    await expect(page.getByLabel('Documentation version')).toBeDisabled();
    await expect(page.getByLabel('Documentation version')).toHaveValue('v1.2.1');
    await page.unroute('**/versions.json');
  }
  await page.goto(base + 'index.html');
  await page.locator('article').getByRole('link', { name: 'English guides', exact: true }).click();
  await page.waitForURL('**/docs/en/index.html');
  await page.locator('article').getByRole('link', { name: '简体中文', exact: true }).click();
  await page.waitForURL('**/docs/zh-CN/index.html');
  await page.locator('article').getByRole('link', { name: 'English', exact: true }).click();
  await page.waitForURL('**/docs/en/index.html');
  await page.goto(base + 'index.html');
  const search = page.locator('input[type="search"], #search-query').first();
  await search.fill('FrameLease');
  await page.waitForFunction(() => [...document.querySelectorAll('a')].some(a => a.href.includes('FrameLease') && a.getClientRects().length > 0));
  const results = await page.locator('a').evaluateAll(links => links.filter(a => a.href.includes('FrameLease') && a.getClientRects().length > 0).map(a => a.href));
  assert(results.some(href => href.startsWith(base + 'api/')));
  await page.goto(results.find(href => href.startsWith(base + 'api/')));
  await page.locator('#documentation-version').waitFor();
  await mkdir(output, { recursive: true });
  for (const language of ['en', 'zh-CN']) {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(base + `docs/${language}/getting-started.html`);
    await expect(page.locator('article pre').first()).toBeVisible();
    await page.screenshot({ path: path.join(output, `${language}-desktop.png`), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(base + `docs/${language}/index.html`);
    await expect(page.locator('article a[href="live-streaming.html"]').first()).toBeVisible();
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Narrow viewport overflows');
    await page.screenshot({ path: path.join(output, `${language}-mobile.png`), fullPage: true });
  }
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
