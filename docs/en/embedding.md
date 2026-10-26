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

The owned viewport clips image, drawing, measurement and HUD content to its layout bounds,
including after zoom, pan and resize. You do not need to set `ClipToBounds` on the public control.
Clicking a viewport takes keyboard focus back from host UI or another viewer; Escape and Delete
apply to the focused viewer's interaction. Each control keeps its own tools, selection and layers.

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

Submission transfers frame ownership immediately, including rejection and cancellation. Fit the image after layout with `FitToViewport()`. Drawing, measurements, HUD, pixel queries, snapshots and menus share the standalone viewer's contracts. Line-profile measurements may open their own plot window. For static images and infrequent changes, use the binding source below; live streams should continue using explicit `SubmitFrameAsync`.

`Accepted` reports reception only, not display completion or permission to recycle storage.
Observe `FrameCommitted` / `OnCommitted` and `DisplayFailed` for later display outcomes;
an accepted frame may be replaced without display. Use `Copy` for reusable producer buffers,
or wait for the owned storage's final-release callback before recycling it. See [frames](frame-pipeline.md).

`Visible` controls this element's own WPF visibility (`false` uses `Hidden`). Use inherited `Visibility`
for XAML bindings or `Collapsed`. `IsVisible` includes ancestor visibility; `IsMinimized` reads the current
containing window, or false without a window. These properties retain WPF thread affinity.
Display work runs only when loaded, effectively visible and not minimized. Input remains accepted while
suspended, with only the newest waiting frame retained. The host controls inactive Tabs using visibility;
there is no special Tab detection. Restoring visibility/loading actively displays the latest input.

## Bind static images

Expose a stable `Fizzy.ImageViewer.Frames.ImageBindingSource` from a ViewModel created on the host UI thread:

```csharp
public ImageBindingSource Image { get; } = new();

// Run on the host UI thread, after producing an immutable buffer.
public void ShowImage(FrameDescriptor descriptor, byte[] pixels)
{
    using var frame = ImageFrame.TakeOwnership(descriptor, pixels, static () => { });
    Image.SetFrame(frame);
    // Do not write pixels again. SetFrame retained an independent reference.
}
```

```xml
<viewer:ImageViewerControl x:Name="ImageViewer" ImageSource="{Binding Image}" />
```

The ViewModel owns and disposes `Image`; the host owns and awaits the control's disposal.
`SetFrame` acquires a reference without consuming the supplied frame or copying its pixels.
Each attached control independently retains the same immutable storage through the existing
frame pipeline. Only the last reference releases storage. Use a real release callback for
pooled/native storage; the empty callback above is for a managed array.

Create, update and dispose the source on its creating UI thread. A source may serve multiple
controls on that same Dispatcher, but cannot cross Dispatchers. Background code may prepare
an `ImageFrame`; retain it until an awaited Dispatcher invocation of `SetFrame` finishes, then
dispose that caller-owned frame. Do not use background setters or `Binding.IsAsync` for this
source. Direct frame submission retains its existing cross-thread support.

Bind the source object, not a disposable `ImageFrame` or `FrameLease`. WPF can defer reading
a ViewModel property: a delayed read observes the source's current image, or empty if the source
was disposed. It never depends on an old frame token remaining valid until a binding callback.
Replacing the ViewModel's source property and disposing the old source on the UI thread is safe;
`PropertyChanged` returning is not an ownership acknowledgement. A stable source with `SetFrame`
usually avoids that extra property notification entirely.

| Action | Result |
| --- | --- |
| Assign the same source again | No resubmission merely because the dependency property was assigned again |
| Call `SetFrame` again, even with the same frame | New reception for each attached control |
| `SetFrame(null)` | Release the source's image reference; controls keep their displayed image |
| Set `ImageSource` to null or clear its binding | Unsubscribe that control; do not dispose the source or clear the display |
| Dispose the source | Empty it and notify controls; reject subsequent updates; delayed bindings see empty |
| Unload/reload a control | Keep its source association and latest waiting frame; resume display on reload |
| Dispose a control | Detach its binding/source and drain its pipeline; other controls and source remain independent |

Emptying or detaching does not retract already accepted input: a waiting or in-flight frame
may still display. No operation above clears measurements. A disposed source retained in a
dependency property is an empty source object, not an invalid ownership token. Reusing a disposed
control is unsupported; assigning a non-null source after stopping throws `ObjectDisposedException`.

`ImageSourceSubmitResult` is a read-only dependency property containing the latest binding
reception result, or null when the source is empty/detached. Bind to its `Status` and `Error` for
UI feedback. It is per control, not per source; `Accepted` does not imply display. Frozen updates
are rejected without automatic retry; call `SetFrame` again when a new reception is wanted.
Observe `FrameCommitted` and `DisplayFailed` for subsequent outcomes and await `DisposeAsync`
for pipeline cleanup failures. Source-owned cleanup failures propagate from `SetFrame` or
`Dispose` after all controls have been notified; the new source value remains installed and
cleanup is not retried. As with other frames, external lease cleanup belongs to its holder.

Binding and explicit submission share the same latest-frame reception order. An explicit
submission can replace a bound image; an unchanged binding does not reassert it. A later
`SetFrame` or binding to another nonempty source submits again. `SubmitFrameAsync` still
consumes its input immediately, including rejection and cancellation.

## WPF commands

Use `ImageViewerCommands.FitToViewport`, `ActivateMeasurementTool` and `EndInteraction` with
buttons, menus or `KeyBinding`. No MVVM framework is required. For a toolbar outside the
control, name the target explicitly; without `CommandTarget`, normal WPF keyboard-focus routing
determines the control. Separate controls never share tool sessions.

```xml
<Button Content="Fit" Command="{x:Static viewer:ImageViewerCommands.FitToViewport}"
        CommandTarget="{Binding ElementName=ImageViewer}" />
<Button Content="Measure" Command="{x:Static viewer:ImageViewerCommands.ActivateMeasurementTool}"
        CommandParameter="{Binding ToolId}" CommandTarget="{Binding ElementName=ImageViewer}" />
<Button Content="End" Command="{x:Static viewer:ImageViewerCommands.EndInteraction}"
        CommandTarget="{Binding ElementName=ImageViewer}" />
```

`ToolId` is a built-in `MeasurementToolIds` value or the ID of a custom tool registered on
that target. `CanExecute` checks stored registration metadata and layer admission, never
creates a session or calls custom tool code. Availability refreshes when registration,
interaction, layer input policy, display/layout or control availability changes.

| Command | Additional availability requirement |
| --- | --- |
| `FitToViewport` | A displayed image and nonempty viewport layout |
| `ActivateMeasurementTool` | Nonblank string naming a registered tool; measurement layer visible, hit-test enabled and not clearing |
| `EndInteraction` | Active creation or editing session; completed measurements and applied edits are preserved |

All three are unavailable while unloaded, hidden, disabled, minimized or stopping. Invalid
parameters for activation are unavailable; Fit/End ignore their parameters. Execution checks
again and does nothing if unavailable. Exceptions from actual tool execution propagate to the
WPF command caller; direct methods keep their existing exception contracts. Calls and command
state queries retain WPF UI-thread affinity.

No global shortcuts are installed. The example assigns Ctrl+F, Ctrl+L and Ctrl+E on the static
viewer. Existing Escape input and built-in menu actions use the same interaction coordinator;
menus retain their captured registration/session identity so delayed clicks cannot affect a
replacement session. End does not add deletion, clearing, saving or disposal operations.

## Threads and temporary unloading

Library viewing methods marshal to the control's dispatcher as needed. Binding sources, dependency properties, commands and inherited WPF members such as layout and visual-tree operations retain WPF thread affinity. Notifications run on the owning UI thread; measurement event senders are the control for embedded use and the `Viewer` for standalone use. Do not synchronously wait for work that needs that dispatcher.

An embedded control shares the application's UI thread. Busy application UI work can delay presentation and interaction, and multiple embedded controls share this cost. Background frame preparation and query work remain separate. Standalone `Viewer` creates the same control on its independent STA. Neither wrapper adds a frame copy or submission queue.

`Unloaded`, including a Tab switch, ends the current interaction and releases mouse capture. Frames and completed measurements remain available; submissions may continue while unloaded. Reattach the same control on the same dispatcher to continue. Unloading is not final disposal, and moving an instance to another dispatcher is unsupported.

## Close asynchronously

The embedding owner stops and awaits its producer, then awaits `ImageViewer.DisposeAsync()` while the host dispatcher is still running. For a normal window, cancel the first `Closing` event, perform asynchronous cleanup, and request final closure after the handler returns. The [complete example](../../samples/EmbeddedViewer/MainWindow.xaml.cs) implements this pattern, prevents duplicate closure, and reports producer and cleanup failures.

Repeated disposal calls share one completion and its success or failure. Disposal stops admission, drains accepted frame/query work and releases owned resources; it never closes the host window or dispatcher. Independent frame leases remain valid. A disposed control is terminal; create a new instance rather than reload it.

If the dispatcher shuts down prematurely, the control attempts to stop and safely clean up on `ShutdownStarted`, and its disposal result reports the lifecycle failure. This fallback does not replace awaited disposal. Unsafe native resources remain isolated rather than freed or reused. Cleanup failures while ending an unloaded interaction are logged and retained for final disposal.

## Application endpoints

Separate pipeline and ViewModel dependencies through application-owned interfaces; see [application integration](application-integration.md) and the [ApplicationEndpoint example](../../samples/ApplicationEndpoint/App.xaml.cs). Direct calls remain suitable for simple windows.

## Run the example

```powershell
dotnet run --project samples/EmbeddedViewer
```

The example has separate bound-static and explicit-live tabs. Next image prepares pixels in
the background and updates its ViewModel's source on the UI thread; the live tab uses explicit
submission. Buttons, menus and shortcuts demonstrate the three commands with explicit targets.
Switch tabs to unload/reload, then close while streaming: the host awaits both producers,
disposes the ViewModel's source and awaits both controls before closing. It needs no camera or GPU.
