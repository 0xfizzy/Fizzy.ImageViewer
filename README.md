# Fizzy.ImageViewer

![Fizzy.ImageViewer icon](assets/icon.svg)

A fast, standalone WPF image viewer for .NET 8. It provides zoom and pan,
shape overlays, measurements, interactive editing, HUD content, and pixel inspection.

[![NuGet](https://img.shields.io/nuget/v/Fizzy.ImageViewer.svg)](https://www.nuget.org/packages/Fizzy.ImageViewer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Install

```powershell
dotnet add package Fizzy.ImageViewer
```

The consuming project must target `net8.0-windows` (or a compatible newer target)
and set `<UseWPF>true</UseWPF>`.

## Quick start

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using var viewer = new Viewer(NullLogger<Viewer>.Instance);
viewer.Title = "Inspection";

await viewer.RefreshAsync(new MyImageSource());

using var crosshair = viewer.DrawCrosshair(
    new Point(320, 240), Brushes.LimeGreen);
```

Implement `IImageSource` to provide pixels without coupling the viewer to an imaging SDK:

```csharp
public sealed class MyImageSource : IImageSource
{
    public int Width => 640;
    public int Height => 480;
    public PixelFormat WpfFormat => PixelFormats.Bgr24;

    public void WriteTo(WriteableBitmap destination)
    {
        // Copy pixels into destination. For hot paths, write through BackBuffer.
    }

    public void Dispose()
    {
        // Release or return the underlying frame buffer.
    }
}
```

Passing an `IImageSource` to `RefreshAsync` transfers ownership to the viewer.
The viewer disposes it after rendering, dropping, freezing, or cancelling the frame.

## Features

- Independent STA window with thread-safe API dispatch
- Low-allocation frame rendering and bounded render queue
- Zoom, pan, fit-to-window, and borderless modes
- Lines, text, crosshairs, rectangles, circles, and HUD text
- Built-in point, line, rectangle, and line-strength measurements
- Shape selection and interactive editing
- Extensible context menu and measurement APIs
- Pixel value inspection

## Build

```powershell
dotnet build -c Release
dotnet pack -c Release -o ./artifacts
```

## License

MIT
