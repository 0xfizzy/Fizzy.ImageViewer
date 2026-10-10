# Getting started

[简体中文](zh-CN/getting-started.md)

Use Windows and .NET 8 with a `net8.0-windows` project and `UseWPF=true`. Viewer creates its own STA; a console application can use the facade without creating an application dispatcher.

1. Create `Viewer(showWindow: false)` and arrange unconditional asynchronous disposal.
2. Subscribe to `Closed` and configure before `Show()`.
3. Create immutable input with `ImageFrame.Copy`. Submit once; submission transfers ownership even if the viewer has closed.
4. Await commit/drop, then show the window.
5. Stop production on closure. Await production and `DisposeAsync`; never block the STA callback waiting for either.

`Closed` is a notification, not proof of render/query drain. Viewer has one window lifetime; create a new instance after closure.

## Complete examples

Run from the repository root. These manual checks open WPF windows:

```powershell
dotnet run --project samples/BasicViewer
dotnet run --project samples/LiveOverlays
dotnet run --project samples/MeasurementsAndExport
```

- [BasicViewer](../samples/BasicViewer/Program.cs): copied grayscale pixels and awaited closure.
- [LiveOverlays](../samples/LiveOverlays/Program.cs): simulated frames, reusable batch handle, production ending before disposal.
- [MeasurementsAndExport](../samples/MeasurementsAndExport/Program.cs): fresh-session point tool, events and PNG output under `artifacts/samples/`.

`./scripts/Test-Samples.ps1` builds without opening windows. Export accepts an optional output directory; choose one without a valuable existing `snapshot.png`.

Continue with [frames](frame-pipeline.md), [lifetime](public-api.md) and [measurements](measurements.md).
