# Fizzy.ImageViewer

View, draw, measure, and inspect images in Windows/.NET 8 WPF applications. Each Viewer owns an independent window; applications provide image acquisition and device integration.

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

Target `net8.0-windows` (or a compatible newer target) and enable `<UseWPF>true</UseWPF>` and implicit usings.

## Quick start

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await closed.Task;
```

Submission transfers ownership immediately, including rejection or cancellation. A committed frame has been accepted by the UI; completion is not a physical presentation timestamp.

## Features

- Zoom, pan, fit-to-window, and borderless display
- Drawing layers, batch markers, and HUD text
- Editable point, length, ROI, and line-profile measurements
- Original-pixel queries, snapshots, and extensible menus

## Documentation

[Guides and API reference](https://0xfizzy.github.io/Fizzy.ImageViewer/)

## License

[MIT](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/main/LICENSE)
