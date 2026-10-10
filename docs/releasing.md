# Release SOP

[简体中文](zh-CN/releasing.md)

For maintainers publishing v1.2.0 or later. Older releases are not backfilled. A release is complete only when NuGet payload, immutable `/v{version}/r0/` documentation, version index and GitHub Release agree. Never move a published tag or overwrite a retained version. NuGet and Pages are separate services; partial publication is recoverable, not atomic.

## One-time setup

1. In GitHub Settings → Pages choose **GitHub Actions**. Set the `github-pages` environment deployment rules to allow main and release tags; the release job runs on tags. Configure approvals according to repository policy.
2. Configure NuGet Trusted Publishing for owner `0xfizzy`, repository `Fizzy.ImageViewer`, workflow `release.yml`, and environment `github-pages` where the policy asks for it. Set repository variable `NUGET_USER`. No persistent API key is stored.
3. Allow the publishing job `contents: write`, `pages: write`, `id-token: write`. PR validation and audit use read permissions. Protect main, release tags and `docs-archive`; allow the publishing bot to append archive commits, prohibit force pushes and deletion. Keep the environment's tag policy consistent with the workflow.
4. Create the initially empty archive branch using `./scripts/Initialize-DocumentationArchive.ps1`. This explicitly pushes a new branch and must be run by an authorized maintainer. It refuses to replace an existing branch.
5. Review the centralized `docs/site.json`. For an isolated rehearsal repository, change this configuration and workflow/trust settings there; never send rehearsal packages to the production NuGet ID. Run offline failure gates first.
6. Have another maintainer follow this SOP in the isolated environment, including a deployment failure after package publication. Record commit, workflow run URLs, observed outcomes and unresolved manual checks in a retained release-readiness issue. A local fault fixture is not a real OIDC/Pages exercise.

## Prepare a release

Update the project version, release notes, affected English XML/guides, Chinese translations and examples in one reviewed PR. Versions are SemVer without build metadata; the project version is `1.2.0`, its tag and documentation directory are `v1.2.0`. A prerelease such as `1.3.0-rc.1` does not replace latest stable.

```powershell
npm ci
npx playwright install chromium
./scripts/Test-Release.ps1 -Version 1.2.0
node scripts/Test-Browser.mjs
./scripts/Serve-Docs.ps1
```

Review `artifacts/release/validation.json`, the transcript, test results, package contents and rendered bilingual pages. Local preparation does not require a tag or a clean checkout and cannot create a publishable bundle. Record interaction checks and required benchmark/consumer validation separately; build success is not physical display, mixed-DPI or device verification.

Merge the PR after checks pass. A maintainer explicitly authorizes publication by creating and pushing the tag at the reviewed main commit:

```powershell
git status --short
git rev-parse HEAD
git tag -a v1.2.0 -m 'release v1.2.0'
git push origin v1.2.0
```

Do not tag until HEAD is the reviewed commit and the checkout is clean. The workflow also checks the project version, tag identity and reachability from `origin/main`.

## Automated stages and success criteria

1. **Prepare**: locked restore, library tests, tool/sample builds, strict docs, fault gates, browser checks and package verification. Pack once; regenerate release API reference from DLL/XML extracted from that exact package. `New-ReleaseBundle` seals checksums and source identity.
2. **Archive**: create a draft GitHub Release and upload `release-bundle.zip`, containing the original nupkg, documentation zip, manifest and validation report. Download and verify before publishing NuGet. Actions artifacts are only temporary transport.
3. **NuGet**: obtain a short-lived OIDC key, publish or download and verify the existing version. Never use `--skip-duplicate`. Compare all payload entries except NuGet's added signature.
4. **Fixed documentation**: restore archived site, merge into `docs-archive/v{version}/r0/`, commit without force, deploy the entire retained site, and verify every immutable online file. An indexed but incomplete version is not selected as latest.
5. **Completion**: mark the version complete, update root/latest to the greatest completed stable SemVer, redeploy and verify. Publish the GitHub Release only after success.

The final workflow step must print `PASS` and the fixed documentation URL. Inspect the GitHub Release (not draft), NuGet version, language navigation, API/search and version selector. Preserve original bundle assets permanently.

Archive and Pages writes share a concurrency group with development deployment. GitHub may replace pending jobs; a cancelled job is never proof of release completion. The daily/manual audit reports retained tags with missing, draft or inconsistent releases.

## Recovery

Prefer the workflow's manual dispatch from main, selecting the existing tag:

```powershell
gh workflow run release.yml --ref main -f tag=v1.2.0
gh run list --workflow release.yml --limit 5
```

If a bundle already exists, the workflow downloads it and does not rebuild. Local diagnosis with authorized GitHub read access:

```powershell
./scripts/Resume-Release.ps1 -Tag v1.2.0
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

Network retries are bounded; indexing or CDN delay ends in a visible failure rather than presumed success. The audit performs no writes. Workflow logs and GitHub Actions failure notifications are the incident entrypoint. For an unrecoverable automation defect in tagged scripts, use a reviewed workflow fix and the retained bundle; do not mutate the tag or archived documentation.

## Documentation corrections without publishing a package

Corrections require a completed release with archived r0; legacy fixed directories are preserved and cannot be converted in place. Prepare requires a clean checkout and creates/switches to a new branch from the active revision source, preserving prior corrections. It proposes the next revision number; use `-Branch` to choose the new branch name. All changes are checked against the original tag.

```powershell
./scripts/Revision-Docs.ps1 -Action Prepare -Tag v1.2.0
# On the prepared branch, edit guides/translations, navigation, sample C# or XML;
# review translations, accept hashes and commit. No runtime/build/tooling changes.
./scripts/Revision-Docs.ps1 -Action Check -Tag v1.2.0 -Revision 1
git push -u origin docs/v1.2.0-r1
./scripts/Revision-Docs.ps1 -Action Start -Tag v1.2.0 -Revision 1 -Branch docs/v1.2.0-r1
./scripts/Revision-Docs.ps1 -Action Status -Tag v1.2.0
./scripts/Revision-Docs.ps1 -Action Resume -Tag v1.2.0 -Revision 1
./scripts/Revision-Docs.ps1 -Action Rollback -Tag v1.2.0 -Revision 0
```

Check requires clean committed source, rejects non-comment runtime changes, and uses the published original DLL with reviewed XML. Samples compile against the original package; strict XML, translation, site and browser checks must pass. The workflow checks allowed changes before executing candidate scripts. Start stores one `documentation-rN.zip` on the original Release and verifies downloaded bytes before deployment. Same-name content conflicts are checked under the shared deployment lock. Never overwrite assets; choose another revision for different content.

The fixed revision is deployed and every file verified before activation. Root/latest only select completed stable versions. Resume downloads the original correction bundle without rebuilding. Rollback verifies the selected completed revision and moves only the active pointer. Re-running an older completed publication cannot reverse a later correction or intentional rollback. Both stages verify online entrypoints; failures remain visible in Actions. Run `./scripts/Test-DocumentationRevision.ps1` for offline immutability, activation, rollback, conflict and source-restriction tests. No correction operation publishes a NuGet package.

For development-only preview, initialize the archive once and dispatch `docs.yml` on main. NuGet credentials and release tags are unnecessary. With no stable version, the homepage links to `/dev/`.
