# Building and maintaining documentation

[简体中文](../zh-CN/documentation.md)

Maintain English XML as the symbol contract, English guides as cross-API protocols, and matching Chinese guides. Do not add runtime behavior to make comments pass. Review ownership, return meaning, cancellation, threading and cleanup against implementation and tests; compiler success is not semantic coverage.

## Build and preview

The DocFX configuration is `docs/docfx.json`; the site homepage and top-level navigation are under `docs/index.md` / `docs/toc.yml`. `Build-Docs` stages inputs under `artifacts/docs/source/` and runs this configuration there, using the output paths defined below.

Install PowerShell 7, the exact SDK in `global.json`, the .NET 8 Desktop Runtime, Node.js 22.14.0 and Git. The library still targets .NET 8; the pinned SDK compiler supports the existing test source. The repository pins DocFX and Playwright. Run from the repository root:

```powershell
npm ci
npx playwright install chromium
./scripts/Build-Docs.ps1
node scripts/Test-Browser.mjs
./scripts/Serve-Docs.ps1
```

`Build-Docs` validates translations, restores locked packages, recompiles strict XML, extracts reference metadata from actual DLL/XML, builds the site, checks HTML links and runs browser smoke. No WPF example, device, publication or deployment is started. API input, inventory and site output stay in `artifacts/docs/`. Preview uses the configured project prefix and `/dev/` or `/v{version}/`; use the printed HTTP URL, not a file URL. Ctrl+C stops preview.

`Test-Browser` runs pinned Chromium against a local server, checking English/Chinese entrypoints, one API page, language switching, search result navigation, version selection, missing-page fallback and page errors. Desktop and narrow-screen review images are saved for both languages; the result report and failure image are in `artifacts/docs/browser/`. Simulated version mounts do not establish that any real release was deployed. After deployment, use `node scripts/Test-Browser.mjs --url https://0xfizzy.github.io/Fizzy.ImageViewer/dev/` for live checks; version fallback remains a local fixture.

Run the full documentation build and browser smoke when changing public XML/declarations, guides/translations, README, navigation, templates or tooling. CI applies the complete gate on every push and pull request. Internal-only changes preserving contracts need no unrelated guide edits. Builds sharing library output must be sequential.

## Translation review

English guides are under `docs/en/`, matching Chinese filenames under `docs/zh-CN/`. Keep counterpart links, API identifiers and example source consistent. The checker requires both language links and both navigation entries, and rejects orphan translations. The package README is an English packaging template, excluded from guide translation pairs.

After reviewing the final source against its translation:

```powershell
./scripts/Test-Documentation.ps1 -AcceptTranslation docs/en/snapshots.md
./scripts/Test-Documentation.ps1
```

`docs/translations.json` records normalized source hashes; newline-only changes do not invalidate review. Never accept a source hash without reviewing the translation. Link checks remain active during acceptance. Review rendered pages in both languages before completion.

## Samples and gates

```powershell
./scripts/Test-Samples.ps1
./scripts/Test-DocumentationGate.ps1
./scripts/Test-ReleaseGate.ps1
pwsh -NoProfile -File scripts/Test-ReleaseRecovery.ps1
./scripts/Test-Workflows.ps1
```

Sample builds do not open windows. Run the [examples](getting-started.md) manually for interaction checks. Documentation fixtures reject missing XML, stale/missing translations and broken links; release fixtures check same-version replacement, stale candidates, stable ordering, payload comparison and restore. Recovery runs the actual orchestration against fake GitHub/NuGet/Pages boundaries in a separate process, including a lost NuGet response and failed Pages deployment. Its report is `artifacts/recovery-fixture/result.json`; this is explicitly a simulation. Workflow checks use checksum-pinned actionlint 1.7.7. Tests use disposable `artifacts/` directories and do not publish real versions.

Dependency changes require an intentional unlocked restore/npm install, review of lockfile diffs, and a subsequent locked gate. Never regenerate locks during release. Generated HTML, API metadata and local reports are not source-controlled.

## Versioned publication

`docs/site.json` is the single source for repository, origin, project prefix, minimum retained version and archive branch. Fixed documentation uses `/v{version}/`; updates replace that directory. Main uses `/dev/`. Each version retains its search, assets, examples and source identity. Git history keeps prior contents for rollback; public revision paths and revision selection do not exist.

The archive branch retains generated sites; GitHub Release assets retain original bundles independently of Actions retention. Do not edit archived version files. Follow the [release SOP](releasing.md) for first-time setup, publication, partial failure and recovery. Building documentation never authorizes publication.

`Serve-Docs.ps1 -SiteDirectory artifacts/docs/site -Port 8087` serves existing output at its true project/version prefix without building or network access. Development deployment rejects a build whose SHA is no longer current main after acquiring the shared deployment lock.

Each newly built snapshot includes `pages.json` with its pages and anchors. Version switching uses that inventory, falls back to the corresponding API or language homepage when a page is absent, and retains only existing anchors. If an older snapshot has no inventory or a request fails, the selector restores the current version and exposes the error in its tooltip; open that version directly if needed. Historical snapshots are not rewritten.

The bilingual homepage is not a translation pair. Guide pairs use identical filenames under `docs/en/` and `docs/zh-CN/`; the NuGet README remains a packaging template. The published homepage is `index.html`, guides retain their language directories, and API output remains under `api/`. Version switching opens the same path if it exists, otherwise the target language homepage or site homepage. Historical archives are not rewritten. Changes to templates, scripts or build configuration require the normal development/release workflow; the documentation-only update gate does not authorize them.
