# Getting started

[简体中文](../zh-CN/getting-started.md)

Build a small application that displays an image, then choose a component guide for your next task.

## Create a viewer

Use Windows and .NET 8 with a `net8.0-windows` project and `<UseWPF>true</UseWPF>`. Create a console project with this project file, then install the package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

```powershell
dotnet add package Fizzy.ImageViewer
```

Viewer creates its own STA; a console application can use it without creating an application dispatcher. With implicit usings enabled, this is a complete console entry point:

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
viewer.Title = "My first image";

var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");

viewer.Show();
await closed.Task;
// await using waits for shutdown and reports cleanup failures.
```

Configure and subscribe before `Show()`. Submission transfers ownership immediately, even when cancelled or closed: do not submit the same frame twice or dispose its transferred storage. `Committed` means the UI accepted the frame, not that the monitor has displayed it. Inspect both `Status` and `Error`, including for dropped frames.

`Closed` is a notification, not proof that rendering and queries have drained. Stop and await your producer before awaiting `DisposeAsync`; never synchronously wait for either from a Viewer callback. A Viewer has one window lifetime; create a new instance after closure.

## Basic operations

Use the wheel to zoom and the middle button to pan. The context menu offers measurements and image export. To add features programmatically, place these calls before `viewer.Show()` in the example:

```csharp
viewer.HudLabelText = "My image";
viewer.IsPixelInfoEnabled = true;
viewer.ActivateMeasurementTool(
    Fizzy.ImageViewer.Measurements.MeasurementToolIds.Length);
```

Click two points to measure a length in image pixels. Escape ends creation or editing. See [measurements](measurements.md) to observe results, [drawing](drawing-layers.md) to add annotations, and [snapshots](snapshots.md) to save image pixels.

## Complete examples

Run from the repository root. These manual checks open WPF windows:

```powershell
dotnet run --project samples/BasicViewer
dotnet run --project samples/LiveOverlays
dotnet run --project samples/MeasurementsAndExport
```

- [BasicViewer](../../samples/BasicViewer/Program.cs): copied grayscale pixels and awaited closure.
- [LiveOverlays](../../samples/LiveOverlays/Program.cs): simulated frames, reusable batch handle, production ending before disposal.
- [MeasurementsAndExport](../../samples/MeasurementsAndExport/Program.cs): fresh-session point tool, events and PNG output under `artifacts/samples/`.

`./scripts/Test-Samples.ps1` builds without opening windows. Export accepts an optional output directory; choose one without a valuable existing `snapshot.png`.

## Advanced integration

Continue with [CPU/GPU frames](frame-pipeline.md), [CUDA integration](cuda.md), or [live-streaming ownership and performance](live-streaming.md).
