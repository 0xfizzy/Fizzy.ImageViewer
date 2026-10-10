// Resolve the version document root from this module, never from the current page depth.
const root = new URL('../../', import.meta.url);
const identity = await fetch(new URL('identity.json', root)).then(r => {
  if (!r.ok) throw new Error('Missing documentation identity');
  return r.json();
});
const banner = document.createElement('div');
banner.id = 'documentation-version';
banner.className = 'documentation-version';
const label = document.createElement('span');
label.textContent = `${identity.channel === 'Development' ? 'Development (unreleased)' : `v${identity.version}${identity.version.includes('-') ? ' (prerelease)' : ''}`} · ${identity.commit.slice(0, 12)}`;
banner.append(label);
const select = document.createElement('select');
select.setAttribute('aria-label', 'Documentation version');
const currentLabel = identity.channel === 'Development' ? 'Development' : `v${identity.version}`;
select.add(new Option(currentLabel, identity.directory));
select.disabled = true;
banner.append(select);
document.body.prepend(banner);
try {
  const response = await fetch(new URL(`${identity.basePath}versions.json`, location.origin), { cache: 'no-store' });
  if (!response.ok) throw new Error('No version index');
  const index = await response.json();
  select.replaceChildren();
  const entries = [...(index.development ? [{ directory: 'dev', label: 'Development' }] : []), ...index.versions.filter(v => v.completed).map(v => ({ directory: `v${v.version}`, label: `v${v.version}` }))];
  if (!entries.some(entry => entry.directory === identity.directory)) entries.unshift({ directory: identity.directory, label: `${identity.directory} (publication pending)` });
  for (const entry of entries) {
    const option = document.createElement('option');
    option.value = entry.directory; option.textContent = entry.label;
    option.selected = entry.directory === identity.directory;
    select.append(option);
  }
  select.disabled = false;
  select.addEventListener('change', async () => {
    select.disabled = true;
    try {
      let relative = decodeURI(location.pathname.slice(root.pathname.length)) || 'index.html';
      const targetRoot = new URL(`${identity.basePath}${select.value}/`, location.origin);
      const response = await fetch(new URL('pages.json', targetRoot));
      if (!response.ok) throw new Error('Page inventory unavailable');
      const pages = await response.json();
      if (!Object.hasOwn(pages, relative)) relative = relative.startsWith('api/') ? 'api/index.html' :
        relative.startsWith('docs/zh-CN/') ? 'docs/zh-CN/index.html' : 'docs/index.html';
      if (!Object.hasOwn(pages, relative)) relative = 'index.html';
      const candidate = new URL(relative, targetRoot);
      const anchor = decodeURIComponent(location.hash.slice(1));
      if (anchor && pages[relative].includes(anchor)) candidate.hash = location.hash;
      location.assign(candidate);
    } catch (error) {
      select.value = identity.directory;
      banner.title = String(error);
    } finally { select.disabled = false; }
  });
} catch (error) { banner.title = String(error); }
