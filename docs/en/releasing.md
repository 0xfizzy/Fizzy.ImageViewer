# Release SOP

[简体中文](../zh-CN/releasing.md)

For maintainers publishing v1.2.0 or later. Older releases are not backfilled. A release is complete only when NuGet payload, `/v{version}/` documentation, version index and GitHub Release agree. Never move a published tag or overwrite a package. Documentation updates replace the same version directory. NuGet and Pages are separate services; partial publication is recoverable, not atomic.

## One-time setup

1. In GitHub Settings → Pages choose **GitHub Actions**. Set the `github-pages` environment deployment rules to allow main and release tags; the release job runs on tags. Configure approvals according to repository policy.
2. Configure NuGet Trusted Publishing for owner `0xfizzy`, repository `Fizzy.ImageViewer`, workflow `release.yml`, and environment `github-pages` where the policy asks for it. Set repository variable `NUGET_USER`. No persistent API key is stored.
3. Allow the publishing job `contents: write`, `pages: write`, `id-token: write`. PR validation and audit use read permissions. Protect main, release tags and `docs-archive`; allow the publishing bot to append archive commits, prohibit force pushes and deletion. Keep the environment's tag policy consistent with the workflow.
4. Create the initially empty archive branch using `./scripts/Initialize-DocumentationArchive.ps1`. This explicitly pushes a new branch and must be run by an authorized maintainer. It refuses to replace an existing branch.
5. Review the centralized `docs/site.json`. For an isolated rehearsal repository, change this configuration and workflow/trust settings there; never send rehearsal packages to the production NuGet ID. Run offline failure gates first.
6. Have another maintainer follow this SOP in the isolated environment, including a deployment failure after package publication. Record commit, workflow run URLs, observed outcomes and unresolved manual checks in a retained release-readiness issue. A local fault fixture is not a real OIDC/Pages exercise.

## Prepare a release

Update the project version, release notes, affected English XML/guides, Chinese translations and examples in one reviewed PR. Versions are SemVer without build metadata; the project version is `1.3.0`, its tag and documentation directory are `v1.3.0`. A prerelease such as `1.3.0-rc.1` does not replace latest stable.

```powershell
npm ci
npx playwright install chromium
./scripts/Test-Release.ps1 -Version 1.3.0
node scripts/Test-Browser.mjs
./scripts/Serve-Docs.ps1
```

Review `artifacts/release/validation.json`, the transcript, test results, package contents and rendered bilingual pages. Local preparation does not require a tag or a clean checkout and cannot create a publishable bundle. Record interaction checks and required benchmark/consumer validation separately; build success is not physical display, mixed-DPI or device verification.

Merge the PR after checks pass. A maintainer explicitly authorizes publication by creating and pushing the tag at the reviewed main commit:

```powershell
git status --short
git rev-parse HEAD
git tag -a v1.3.0 -m 'release v1.3.0'
git push origin v1.3.0
```

Do not tag until HEAD is the reviewed commit and the checkout is clean. The workflow also checks the project version, tag identity and reachability from `origin/main`.

## Automated stages and success criteria

1. **Prepare**: locked restore, library tests, tool/sample builds, strict docs, fault gates, browser checks and package verification. Pack once; regenerate release API reference from DLL/XML extracted from that exact package. `New-ReleaseBundle` seals checksums and source identity.
2. **Archive**: create a draft GitHub Release and upload `release-bundle.zip`, containing the original nupkg, documentation zip, manifest and validation report. Download and verify before publishing NuGet. Actions artifacts are only temporary transport.
3. **NuGet**: obtain a short-lived OIDC key, publish or download and verify the existing version. Never use `--skip-duplicate`. Compare all payload entries except NuGet's added signature.
4. **Fixed documentation**: restore archived site, merge into `docs-archive/v{version}/`, commit without force, deploy the entire retained site, and verify every online file. The candidate includes the completed stable-version index, but the successful-deployment marker is recorded only after online verification.
5. **Completion**: record the successful archive commit. Root/latest select the greatest completed stable SemVer in the same deployment. Publish the GitHub Release only after success.

The final workflow step must print `PASS` and the fixed documentation URL. Inspect the GitHub Release (not draft), NuGet version, language navigation, API/search and version selector. Preserve original bundle assets permanently.

Archive and Pages writes share a concurrency group with development deployment. GitHub may replace pending jobs; a cancelled job is never proof of release completion. The daily/manual audit reports retained tags with missing, draft or inconsistent releases.

## Recovery

Prefer the workflow's manual dispatch from main, selecting the existing tag:

```powershell
gh workflow run release.yml --ref main -f tag=v1.3.0
gh run list --workflow release.yml --limit 5
```

Manual recovery uses the reviewed workflow and scripts from main, while package/source identity remains the original tag. Preparation needs `contents: write` to see draft Release assets; PR validation remains read-only. If a bundle already exists, the workflow downloads it and does not rebuild. If NuGet already contains the version but no archive is visible, preparation stops. Local diagnosis with access to the Release assets:

```powershell
./scripts/Resume-Release.ps1 -Tag v1.3.0
./scripts/Test-PublishedRelease.ps1 -Bundle artifacts/bundle -RequireCompleted
./scripts/Test-ReleaseStatus.ps1
```

`Resume-Release` only restores and verifies; the workflow continues publication and deployment with appropriate permissions. Repeated successful runs leave fixed contents unchanged and do not downgrade latest. Asset identity conflicts always stop.

| Failure | Action and expected result |
| --- | --- |
| Validation before archive | No package was published. Fix source and review a new unused release identity; do not move the old tag automatically. |
| Interrupted archive upload | Check whether the single bundle asset exists. If present, recover it; otherwise rerun the original successful preparation artifact. Same-name different content is rejected. |
| NuGet request timeout | Query/download first. Matching payload means continue; absent/indexing output stays incomplete and can be retried. |
| Existing NuGet version differs | Stop; investigate identity conflict and choose a new version. Never delete or overwrite to force success. |
| NuGet succeeds, Pages fails | Keep the draft and original assets; dispatch the same tag. No repack is performed. |
| Online file/identity mismatch | Do not mark completion. Inspect archive commit and deployment/CDN state, then retry. |
| Latest/index deployment fails | Fixed version remains; same-tag recovery republishes the verified index. |
| GitHub completion response lost | Inspect actual draft status, then rerun. |
| Actions artifact expired | Recover from durable GitHub Release bundle and archive branch. |
| Original bundle missing after package publication | Stop and investigate; do not rebuild a replacement pretending to be the original. |

Network retries are bounded; indexing or CDN delay ends in a visible failure rather than presumed success. The audit performs no writes. Workflow logs and GitHub Actions failure notifications are the incident entrypoint. For an unrecoverable automation defect in tagged scripts, use a reviewed workflow fix and the retained bundle; do not mutate the tag or original Release assets.

Pages deployment identifies each build by its documentation archive commit, not the package source commit. Development and fixed-version updates can share source code but contain different sites. Artifact names include the stage, run and attempt to avoid collisions on retries. Online content verification remains required after the deployment API reports success.

## Optional CUDA package

Publish `Fizzy.ImageViewer.Cuda` separately from the same source tag after the core package is available. On a Windows machine with the [CUDA build prerequisites](cuda.md#build-and-deployment), prepare from the tag's exported source, compile native tests without running hardware, run managed CUDA tests, and seal the original package:

```powershell
./scripts/New-CudaReleaseBundle.ps1 -Tag v1.3.0
./scripts/Publish-CudaRelease.ps1 -Stage Archive -Tag v1.3.0 -Bundle artifacts/cuda-release-preparation/bundle
gh workflow run release.yml --ref main -f tag=v1.3.0 -f component=cuda
```

Review the preparation transcript, test results and bundle validation before Archive. The separate immutable `cuda-release-bundle.zip` asset contains the package, native hash, source identity and validation report. It does not replace the core bundle or fixed documentation. The workflow downloads and validates this asset, publishes through the same NuGet Trusted Publishing policy, and compares the downloaded NuGet payload. The policy must authorize the CUDA package ID. Resume with the same dispatch; never rebuild an already published version. Hardware, driver and physical display validation are separate from managed tests and native compilation.

## Documentation updates without publishing a package

A completed release and its original package bundle are required. Updates replace `/v{version}/` as a whole; removed pages disappear. Prepare requires a clean checkout, creates an isolated worktree and branch from the currently deployed documentation source, and captures the archive SHA. `-Branch` selects the new branch name. Source changes are compared against the original tag.

```powershell
./scripts/Update-Docs.ps1 -Action Prepare -Tag v1.3.0 -Branch docs/v1.3.0-update
# Edit guides/translations, navigation, sample C# or XML comments.
# Review translations, accept hashes, commit; do not change runtime/build/tooling.
./scripts/Update-Docs.ps1 -Action Check -Tag v1.3.0
# Keep running this script from main; Check automatically selects the prepared source.
git -C <prepared-source-directory> push -u origin docs/v1.3.0-update
./scripts/Update-Docs.ps1 -Action Start -Tag v1.3.0 -SourceRef (git -C <prepared-source-directory> rev-parse HEAD)
./scripts/Update-Docs.ps1 -Action Status
./scripts/Update-Docs.ps1 -Action Resume -RunId 123456789
./scripts/Update-Docs.ps1 -Action Rollback -Tag v1.3.0 -ArchiveCommit <successful-archive-sha>
```

Check requires clean committed source and rejects runtime changes before running trusted main tooling. API reference uses the original package DLL with reviewed XML; samples compile against that package. XML, translations, links and Chromium checks must pass. No update modifies NuGet, tags or original Release assets.

Start requires a full source SHA. If Prepare was not used, Start captures the current archive SHA. Under the shared deployment lock, a changed archive baseline rejects the candidate. Development deployment also rejects an obsolete main SHA. Run payloads and hashes are durably committed beneath `.publication/runs/<run-id>/` on `docs-archive`; this internal history is excluded from the public site. The branch retains all original payloads in Git history. Version metadata stores the source commit and file hashes; no revision number is assigned.

Build and checks precede one whole-site Pages deployment. Only verified deployments enter `.publication/state.json` with their successful archive SHA. Failed candidates remain resumable and are never used as the base of an unrelated deployment. Resume reuses the persisted run payload without rebuilding and rejects a candidate superseded by a later successful deployment; obsolete development candidates are also rejected. Rollback restores only the requested version from a known successful archive commit and appends a new commit. It does not rewrite history. A successful old package-release retry cannot undo later documentation updates.

Root/latest select the highest completed stable package version. Updating an older version does not change that selection. Online verification checks candidate bytes and entrypoints after the deployment API reports success; failures remain visible in Actions. Run `./scripts/Test-DocumentationUpdate.ps1` for overwrite, deletion, recovery, rollback, stale candidate and XML-only source fixtures.

Legacy revision directories are removed when the archive is next assembled. Existing active content moves to the direct version path with corrected identity and selector; old revision URLs return 404 without redirection. Original Release assets remain untouched.

For development-only preview, initialize the archive once and dispatch `docs.yml` on main. No package or release tag is needed. With no stable version, the homepage links to `/dev/`.
