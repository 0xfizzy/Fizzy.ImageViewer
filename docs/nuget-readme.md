# Fizzy.ImageViewer

View, draw, measure, and inspect images in Windows/.NET 8 and .NET 10 WPF applications. Embed `ImageViewerControl` in an existing WPF window, or use `Viewer` with an independent window and STA. Applications provide image acquisition and device integration.

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

This package includes `net10.0-windows` and `net8.0-windows` assemblies. Target either framework (or a compatible newer target) and enable `<UseWPF>true</UseWPF>` and implicit usings.

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
if (result.Status != FrameSubmitStatus.Accepted)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await closed.Task;
```

Submission transfers ownership immediately, including rejection or cancellation. `Accepted` means reception only, not display completion or permission to recycle the buffer. Hidden viewers retain the newest input; observe `FrameCommitted` and `DisplayFailed` for subsequent display outcomes. Recycle owned storage only from its final-release callback, or use `ImageFrame.Copy` for reusable producer buffers.

## Embed in WPF

```xml
<viewer:ImageViewerControl
    xmlns:viewer="clr-namespace:Fizzy.ImageViewer;assembly=Fizzy.ImageViewer"
    x:Name="ImageViewer"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

Create on the host STA and call the same frame, drawing and measurement APIs.
Unloading ends interaction but preserves content for reattachment on the same dispatcher.
The owner must await `ImageViewer.DisposeAsync()` before stopping the host dispatcher;
disposal never closes the host window. See the
[embedding guide and complete shutdown example](https://0xfizzy.github.io/Fizzy.ImageViewer/en/embedding.html)
([中文](https://0xfizzy.github.io/Fizzy.ImageViewer/zh-CN/embedding.html)).

## Features

- Zoom, pan, fit-to-window, and borderless display
- Drawing layers, batch markers, and HUD text
- Editable point, length, ROI, and line-profile measurements
- Original-pixel queries, snapshots, and extensible menus

## Documentation

[Guides and API reference](https://0xfizzy.github.io/Fizzy.ImageViewer/)

## License

[MIT](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/main/LICENSE)
