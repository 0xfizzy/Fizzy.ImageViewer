# Validation

[简体中文](../zh-CN/validation.md)

Use Windows, PowerShell 7 and the .NET 10 SDK selected by `global.json`. This repository builds independently; no camera or robot is required. The library and tests target both `net10.0-windows` and `net8.0-windows`; install both Desktop Runtimes to run the tests. Serialize commands sharing library output. Automated WPF tests use hidden windows and injected boundaries; they do not prove physical display, mixed-DPI, remote-desktop or native GPU behavior.

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

Run measurement/interaction/layer suites for session or lifetime changes. Exercise independent activations, factory reentry and failure, ended contexts, preview cleanup, exactly-once session disposal, notification ordering, programmatic edits, control-point crossing, registration revocation and external leases surviving Viewer closure. Preserve resource/thread safety coverage when removing unsupported recovery behavior.

## Architectural boundaries

Public API tests review exported signatures, nullability and defaults against `PublicApi.txt`. Review diffs rather than blindly updating the baseline. Public API or behavior changes in the joint workspace also require the consumers' Source builds and relevant tests according to parent rules. XML-only edits do not change signatures or authorize unrelated runtime changes.

Startup/shutdown tests must check original errors, safe cleanup after partial composition, shared terminal failure, no repeated destructive disposal, native-resource quarantine and actual STA exit. Menu tests cover Closed-before-Click, reopening, revocation, independent action resources and asynchronous failure.

## Line-profile allocations

Run affected benchmarks for hot-path changes; these synthetic measurements do not establish physical presentation timing or sustained device-stream performance.

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --snapshots artifacts/snapshot-copy-benchmark.json
dotnet run --project tools/DrawingBenchmark -c Release
dotnet run --project tools/ResizeBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release -- --query-publication
```

Frame/snapshot modes report time and managed allocation, not total process peak memory. Drawing measurements exclude consumer result construction, cross-thread dispatch and physical presentation. Line-profile modes separate data changes, WPF commands, offscreen rendering and query-result copying; immutable result ownership must remain intact.

## Tool sessions and layer ownership

Validate drawing replacement across single/collection/empty content, visual and click identity, invalid replacement retaining content, whole-layer clear, reentrant callbacks and handle invalidation. Verify HUD survives business-layer clear. Tool tests cover cancellation/disposal failures without terminating a replacement session. Geometry and query tests retain half-open pixel coverage, extreme finite coordinates and non-finite sample gaps.

## Manual checks

Run the three [examples](getting-started.md) and record OS, display scale, monitor arrangement and commit. Check first display, wheel zoom, middle-button pan across overlays, tool activation/cancellation, selection/edit/delete, layer visibility, line-profile closure, HUD, menu export and window close while streaming. Repeat across DPI changes and remote desktop where supported. Real GPU attachment, CPU/GPU switching and devices require a separately authorized environment. Report any unperformed scenario explicitly.

Inspect rendered English/Chinese guides, search, direct nested API links and version selection at the project prefix. Publication and recovery additionally require the [release SOP](releasing.md), fault gates and an isolated service rehearsal. Local tests never authorize a production release.

## Drawing benchmark scope

Validate sustained workloads using allocated bytes per update, GC collection
counts, and update latency percentiles, including the expected marker/text count
and zoom behavior. The drawing benchmark also measures average allocated
bytes and time across 50 warmed-up replacements on the viewer thread, alternating
between two prebuilt circle collections with different positions. It excludes
consumer result creation, cross-thread dispatch, and
physical presentation; it does not establish sustained camera-stream GC performance.
