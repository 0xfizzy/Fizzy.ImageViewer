# Embed in a WPF window

[简体中文](../zh-CN/embedding.md)

Use `ImageViewerControl` when the image belongs inside an existing window, Grid or TabControl. Use `Viewer` for an independent window with its own STA. Borrow `IViewerCapabilities` for the complete view or individual capability interfaces for narrower use. The owner retains `IViewer` for disposal; only the standalone `Viewer` implements `IViewerWindow`.

## Place and configure

Target `net8.0-windows` or `net10.0-windows` with `UseWPF` enabled, and reference Fizzy.ImageViewer. Place the control in your window:

```xml
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:viewer="clr-namespace:Fizzy.ImageViewer;assembly=Fizzy.ImageViewer">
    <Grid>
        <viewer:ImageViewerControl x:Name="ImageViewer" />
    </Grid>
</Window>
```

The parameterless constructor supports XAML. Code can use `new ImageViewerControl(logger)` with an `ILogger<ImageViewerControl>`; null disables logging. Create the control on its host's STA. It is ready to accept frames before `Loaded`, and must be disposed even if never loaded. The library owns `Content`; replacement is rejected. Use ordinary WPF layout properties, not a replacement content or template.

After `InitializeComponent`, use the same viewing methods as on `Viewer`:

```csharp
ImageViewer.HudLabelText = "Camera";
var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await ImageViewer.SubmitFrameAsync(
    Fizzy.ImageViewer.Frames.ImageFrame.Copy(
        new(640, 480, 640, Fizzy.ImageViewer.Frames.FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != Fizzy.ImageViewer.Frames.FrameSubmitStatus.Accepted)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
```

Submission transfers frame ownership immediately, including rejection and cancellation. Fit the image after layout with `FitToViewport()`. Drawing, measurements, HUD, pixel queries, snapshots and menus share the standalone viewer's contracts. Line-profile measurements may open their own plot window. There are no frame-binding dependency properties or viewer commands; call the methods from your application adapter.

`Visible` controls this element's own WPF visibility (`false` uses `Hidden`). Use inherited `Visibility`
for XAML bindings or `Collapsed`. `IsVisible` includes ancestor visibility; `IsMinimized` reads the current
containing window, or false without a window. These properties retain WPF thread affinity.
Display work runs only when loaded, effectively visible and not minimized. Input remains accepted while
suspended, with only the newest waiting frame retained. The host controls inactive Tabs using visibility;
there is no special Tab detection. Restoring visibility/loading actively displays the latest input.

## Threads and temporary unloading

Library viewing operations marshal to the control's dispatcher as needed. Inherited WPF members such as layout and visual-tree operations retain WPF thread affinity. Notifications run on the owning UI thread; measurement event senders are the control for embedded use and the `Viewer` for standalone use. Do not synchronously wait for work that needs that dispatcher.

An embedded control shares the application's UI thread. Busy application UI work can delay presentation and interaction, and multiple embedded controls share this cost. Background frame preparation and query work remain separate. Standalone `Viewer` creates the same control on its independent STA. Neither wrapper adds a frame copy or submission queue.

`Unloaded`, including a Tab switch, ends the current interaction and releases mouse capture. Frames and completed measurements remain available; submissions may continue while unloaded. Reattach the same control on the same dispatcher to continue. Unloading is not final disposal, and moving an instance to another dispatcher is unsupported.

## Close asynchronously

The embedding owner stops and awaits its producer, then awaits `ImageViewer.DisposeAsync()` while the host dispatcher is still running. For a normal window, cancel the first `Closing` event, perform asynchronous cleanup, and request final closure after the handler returns. The [complete example](../../samples/EmbeddedViewer/MainWindow.xaml.cs) implements this pattern, prevents duplicate closure, and reports producer and cleanup failures.

Repeated disposal calls share one completion and its success or failure. Disposal stops admission, drains accepted frame/query work and releases owned resources; it never closes the host window or dispatcher. Independent frame leases remain valid. A disposed control is terminal; create a new instance rather than reload it.

If the dispatcher shuts down prematurely, the control attempts to stop and safely clean up on `ShutdownStarted`, and its disposal result reports the lifecycle failure. This fallback does not replace awaited disposal. Unsafe native resources remain isolated rather than freed or reused. Cleanup failures while ending an unloaded interaction are logged and retained for final disposal.

## Run the example

```powershell
dotnet run --project samples/EmbeddedViewer
```

The example streams synthetic pixels into a XAML control. Use the toolbar to fit or measure, switch tabs to unload/reload, and close while streaming to exercise asynchronous shutdown. It needs no camera or GPU.
