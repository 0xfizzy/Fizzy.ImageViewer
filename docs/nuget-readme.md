# Fizzy.ImageViewer

View, measure, and inspect images in WPF applications. A standalone .NET 8 image
viewer with zoom and pan, drawing overlays, interactive measurements, HUD content,
and pixel inspection.

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

The consuming project must target `net8.0-windows` (or a compatible newer target)
and set `<UseWPF>true</UseWPF>`.

## Quick start

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer();
var pixels = new byte[640 * 480];
var frame = ImageFrame.Copy(new FrameDescriptor(640, 480, 640,
    FramePixelFormat.Gray8), pixels);
var result = await viewer.SubmitFrameAsync(frame);
```

Submission transfers frame ownership immediately. Awaiting returns a commit/drop
result, not a physical presentation timestamp. The viewer runs on its own STA
thread and dispatches public API calls to it.

## Features

- Zoom, pan, fit-to-window, and borderless modes
- Drawing layers with individual shapes and batched markers
- Interactive point, line, region, and line-profile measurements
- Custom measurement tools, shape editing, and change notifications
- Pixel queries, HUD content, snapshots, and extensible context menus

## Documentation

- [Public API and viewer lifetime](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/v1.1.1/docs/public-api.md)
- [Frame ownership, rendering, and snapshots](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/v1.1.1/docs/frame-pipeline.md)
- [Drawing layers](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/v1.1.1/docs/drawing-layers.md)
- [Measurements and interaction](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/v1.1.1/docs/measurements.md)

## License

[MIT](https://github.com/0xfizzy/Fizzy.ImageViewer/blob/v1.1.1/LICENSE)
