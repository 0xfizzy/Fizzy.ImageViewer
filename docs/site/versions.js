// Resolve the version document root from this module, never from the current page depth.
const root = new URL('../../', import.meta.url);
const identity = await fetch(new URL('identity.json', root)).then(r => {
  if (!r.ok) throw new Error('Missing documentation identity');
  return r.json();
});
const banner = document.createElement('div');
banner.id = 'documentation-version';
banner.style.cssText = 'padding:.6rem 1rem;border-bottom:1px solid #888;display:flex;gap:1rem;align-items:center';
const label = document.createElement('span');
label.textContent = `${identity.channel === 'Development' ? 'Development (unreleased)' : `v${identity.version}${identity.version.includes('-') ? ' (prerelease)' : ''}`} · ${identity.commit.slice(0, 12)}`;
banner.append(label);
document.body.prepend(banner);
try {
  const response = await fetch(new URL(`${identity.basePath}versions.json`, location.origin), { cache: 'no-store' });
  if (!response.ok) throw new Error('No version index');
  const index = await response.json();
  const select = document.createElement('select');
  select.setAttribute('aria-label', 'Documentation version');
  const entries = [...(index.development ? [{ directory: 'dev', label: 'Development' }] : []), ...index.versions.filter(v => v.completed).map(v => ({ directory: `v${v.version}`, label: `v${v.version}` }))];
  if (!entries.some(entry => entry.directory === identity.directory)) entries.unshift({ directory: identity.directory, label: `${identity.directory} (publication pending)` });
  for (const entry of entries) {
    const option = document.createElement('option');
    option.value = entry.directory; option.textContent = entry.label;
    option.selected = entry.directory === identity.directory;
    select.append(option);
  }
  select.addEventListener('change', async () => {
    const relative = location.pathname.slice(root.pathname.length);
    const targetRoot = new URL(`${identity.basePath}${select.value}/`, location.origin);
    const candidate = new URL(relative || 'index.html', targetRoot);
    const available = await fetch(candidate, { method: 'HEAD' }).then(r => r.ok).catch(() => false);
    if (available) { candidate.hash = location.hash; location.assign(candidate); }
    else location.assign(new URL(relative.startsWith('docs/zh-CN/') ? 'docs/zh-CN/index.html' : 'index.html', targetRoot));
  });
  banner.append(select);
} catch { /* A standalone version can still be read before its archive index is deployed. */ }
