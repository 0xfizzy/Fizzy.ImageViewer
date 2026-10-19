# Validation

[简体中文](../zh-CN/validation.md)

Use Windows, PowerShell 7 and the .NET 10 SDK selected by `global.json`. This repository builds independently; no camera or robot is required. The library and tests target both `net10.0-windows` and `net8.0-windows`; install both Desktop Runtimes to run the tests. Serialize commands sharing library output. Automated WPF tests use hidden windows and injected boundaries; they do not prove physical display, mixed-DPI, remote-desktop or native GPU behavior.

Select checks below for task requirements, applicable gates and changed behavior. Additional checks require a specific defect or a call, data or resource dependency from the change to the behavior being checked. The scenarios below are coverage choices, not a checklist for every change. Report performed checks and results, plus any required checks left incomplete; do not list unrelated untested scenarios.

## Build and test

```powershell
dotnet restore src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj --locked-mode
dotnet build src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj -c Release --no-restore
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
./scripts/Test-Samples.ps1
```

Use `./scripts/Test-Release.ps1` for the complete library/tool/sample/documentation/package gate. It does not publish. [Documentation maintenance](documentation.md) lists prerequisites and browser installation. Inspect the generated package README, XML and immutable version URL before release. Only release-mode validation at a clean matching tag can create a publishable bundle.

## Pixel queries

Run pixel-source, frame metadata, scheduler, HUD and snapshot tests for format, provenance or query changes. Verify independent leases, local region coordinates, immutable results, unsupported capabilities, subscription/geometry invalidation, bounded admission, fairness, cancellation and original-operation plus cleanup failures. TIFF readback tests use a test-only LibTIFF dependency, not the writer itself as the oracle.

## Measurement ownership and interaction

For session or lifetime changes, select measurement/interaction/layer tests covering the changed behavior and its direct dependencies. Coverage includes independent activations, factory reentry and failure, ended contexts, preview cleanup, exactly-once session disposal, notification ordering, programmatic edits, control-point crossing, registration revocation and external leases surviving Viewer closure. Preserve resource/thread safety coverage when removing unsupported recovery behavior.

## Architectural boundaries

Public API tests review exported signatures, nullability and defaults against `PublicApi.txt`. Review diffs rather than blindly updating the baseline. Public API or behavior changes in the joint workspace also require the consumers' Source builds and relevant tests according to parent rules. XML-only edits do not change signatures or authorize unrelated runtime changes.

Startup/shutdown tests must check original errors, safe cleanup after partial composition, shared terminal failure, no repeated destructive disposal, native-resource quarantine and actual STA exit. Menu tests cover Closed-before-Click, reopening, revocation, independent action resources and asynchronous failure.

## Line-profile allocations

Run corresponding benchmarks when changing hot-path allocation, copying, queuing, synchronization or scheduling; renames, file moves and comment-only edits do not trigger benchmarks. These synthetic measurements do not establish physical presentation timing or sustained device-stream performance.

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --embedded artifacts/frame-benchmark-embedded.json
dotnet run --project tools/FrameBenchmark -c Release -- --snapshots artifacts/snapshot-copy-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --notifications artifacts/notification-benchmark.json
dotnet run --project tools/DrawingBenchmark -c Release
dotnet run --project tools/ResizeBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release -- --query-publication
```

Notification mode measures the actual control notification entry on its STA, including
standalone forwarding, with 0/1/4 external subscribers. It excludes subscription setup,
frame rendering and dispatcher scheduling, and reports warmed samples of per-notification
time and thread allocation. It also measures the shared measurement notification queue
with 0/1/4 subscribers and a prebuilt geometry payload, excluding measurement mutation
and presentation. Compare repeated runs on the same machine and configuration.
Frame/snapshot modes report time and managed allocation, not total process peak memory. Drawing measurements exclude consumer result construction, cross-thread dispatch and physical presentation. Line-profile modes separate data changes, WPF commands, offscreen rendering and query-result copying; immutable result ownership must remain intact.

## Tool sessions and layer ownership

Validate drawing replacement across single/collection/empty content, visual and click identity, invalid replacement retaining content, whole-layer clear, reentrant callbacks and handle invalidation. Verify HUD survives business-layer clear. Tool tests cover cancellation/disposal failures without terminating a replacement session. Geometry and query tests retain half-open pixel coverage, extreme finite coordinates and non-finite sample gaps.

## Manual checks

When the task requires interactive acceptance, or a changed interaction depends on a physical window or display environment not covered by automated tests, select the affected scenarios below. Run the relevant [examples](getting-started.md) and record OS, display scale, monitor arrangement and commit. Check first display, wheel zoom, middle-button pan across overlays, tool activation/cancellation, selection/edit/delete, layer visibility, line-profile closure, HUD, menu export and window close while streaming. Repeat across DPI changes and remote desktop where supported. Real GPU attachment, CPU/GPU switching and devices require a separately authorized environment. Report required scenarios that were not performed.

For documentation changes, use the change-specific gates in [documentation maintenance](documentation.md). Publication and recovery additionally require the [release SOP](releasing.md), fault gates and an isolated service rehearsal. Local tests never authorize a production release.

## Drawing benchmark scope

Validate sustained workloads using allocated bytes per update, GC collection
counts, and update latency percentiles, including the expected marker/text count
and zoom behavior. The drawing benchmark also measures average allocated
bytes and time across 50 warmed-up replacements on the viewer thread, alternating
between two prebuilt circle collections with different positions. It excludes
consumer result creation, cross-thread dispatch, and
physical presentation; it does not establish sustained camera-stream GC performance.

## Build output layout

Outputs are rooted at this repository's `build/Debug/` or `build/Release/`. Independent libraries and tools use project subdirectories (with framework/RID subdirectories where needed). Test binaries and fixtures use `build/<Configuration>/tests/<Project>/`; test results use that project's `TestResults/` directory. Intermediate and restore files use `build/obj/`, isolated by project and configuration. Source dependencies keep their own repository output roots.

Builds sharing outputs must run serially. `artifacts/` is reserved for temporary logs, reports, package staging and release validation. Old project-local `bin/` and `obj/` trees are excluded from compilation; remove them only after checking that they are no longer in use.
