using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using Fizzy.ImageViewer.Frames;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ImageBindingTests
{
    private static ImageFrame Frame(byte value = 42, Action? release = null) => ImageFrame.TakeOwnership(
        new(1, 1, 1, FramePixelFormat.Gray8), new[] { value }, release ?? (() => { }));

    private static void Bind(ImageViewerControl control, Model model) => control.SetBinding(
        ImageViewerControl.ImageSourceProperty, new Binding(nameof(Model.Image)) { Source = model });

    private static async Task Pump() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static async Task Display(ImageViewerControl control, Action update)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<FrameInfo> handler = (_, _) => done.TrySetResult();
        control.FrameCommitted += handler;
        try { update(); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { control.FrameCommitted -= handler; }
    }

    [Fact]
    public Task XamlBindingReplacementClearAndExplicitSubmissionSharePipeline() => ControlTestDispatcher.Run(async () =>
    {
        using var first = new ImageBindingSource();
        using var second = new ImageBindingSource();
        var model = new Model { Image = first };
        await using var control = (ImageViewerControl)XamlReader.Parse(
            "<v:ImageViewerControl xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:v='clr-namespace:Fizzy.ImageViewer;assembly=Fizzy.ImageViewer' ImageSource='{Binding Image}' />");
        var window = new Window { Content = control, Width = 200, Height = 200, ShowActivated = false };
        int released = 0;
        try
        {
            control.DataContext = model;
            window.Show(); await Pump();
            using (var frame = Frame(release: () => released++))
            {
                await Display(control, () => first.SetFrame(frame));
                Assert.Equal(42, frame.AcquireAndRead()); // SetFrame did not consume ownership.
            }
            Assert.Equal(FrameSubmitStatus.Accepted, control.ImageSourceSubmitResult?.Status);
            var receipt = control.ImageSourceSubmitResult;
            model.Image = first; await Pump();
            Assert.Equal(receipt, control.ImageSourceSubmitResult);
            using (var frame = Frame(71)) second.SetFrame(frame);
            await Display(control, () => model.Image = second);
            first.Dispose();
            Assert.Equal(1, released);
            using (var current = control.AcquireCurrentFrame()) Assert.Equal(71, current!.CpuPixels.Span[0]);
            BindingOperations.ClearBinding(control, ImageViewerControl.ImageSourceProperty);
            Assert.Null(control.ImageSource);
            Assert.Null(control.ImageSourceSubmitResult);
            using (var frame = Frame(90)) second.SetFrame(frame);
            using (var current = control.AcquireCurrentFrame()) Assert.Equal(71, current!.CpuPixels.Span[0]);
            await Display(control, () => Bind(control, model));
            await control.SubmitAndDisplayAsync(Frame(99));
            await Pump();
            using (var current = control.AcquireCurrentFrame()) Assert.Equal(99, current!.CpuPixels.Span[0]);
            second.SetFrame(null);
            model.Image = null; await Pump();
            using (var current = control.AcquireCurrentFrame()) Assert.Equal(99, current!.CpuPixels.Span[0]);
        }
        finally { await control.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task DeferredBindingReadsLatestOrDisposedSourceWithoutRetainingDeadToken() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        int released = 0;
        using (var frame = Frame(release: () => released++)) source.SetFrame(frame);
        await using var control = new ImageViewerControl();
        // No DataContext yet: WPF has not read the image source.
        control.SetBinding(ImageViewerControl.ImageSourceProperty, new Binding(nameof(Model.Image)));
        source.Dispose();
        Assert.Equal(1, released);
        control.DataContext = new Model { Image = source };
        await Pump();
        Assert.Same(source, control.ImageSource);
        Assert.Null(control.ImageSourceSubmitResult);
        using var next = new ImageBindingSource();
        using (var frame = Frame(64)) next.SetFrame(frame);
        using (var frame = Frame(65)) next.SetFrame(frame);
        control.DataContext = new Model { Image = next };
        await Pump();
        Assert.Equal(FrameSubmitStatus.Accepted, control.ImageSourceSubmitResult?.Status);
        await control.DisposeAsync();
        Assert.Null(control.ImageSource);
        Assert.False(BindingOperations.IsDataBound(control, ImageViewerControl.ImageSourceProperty));
        using var remaining = next.AcquireFrame();
        Assert.Equal(65, remaining!.AcquireAndRead());
    });

    [Fact]
    public Task SharedSourceUnloadReloadAndIndependentCloseRetainReferences() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        await using var first = new ImageViewerControl { Height = 120 };
        await using var second = new ImageViewerControl { Height = 120 };
        var model = new Model { Image = source };
        Bind(first, model); Bind(second, model);
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        var window = new Window { Content = panel, Width = 300, Height = 350, ShowActivated = false };
        int released = 0;
        try
        {
            window.Show(); await Pump();
            var a = Display(first, () => { }); var b = Display(second, () => { });
            using (var frame = Frame(release: () => released++)) source.SetFrame(frame);
            await Task.WhenAll(a, b);
            using var external = first.AcquireCurrentFrame();
            panel.Children.Remove(first); await Pump();
            Assert.Same(source, first.ImageSource);
            await Display(second, () => { using var frame = Frame(84); source.SetFrame(frame); });
            await Display(first, () => panel.Children.Add(first));
            using (var current = first.AcquireCurrentFrame()) Assert.Equal(84, current!.CpuPixels.Span[0]);
            await first.DisposeAsync();
            Assert.Null(first.ImageSource);
            source.Dispose();
            using (var current = second.AcquireCurrentFrame()) Assert.Equal(84, current!.CpuPixels.Span[0]);
            await second.DisposeAsync();
            Assert.Equal(0, released);
            Assert.Equal(42, external!.CpuPixels.Span[0]);
            external.Dispose(); Assert.Equal(1, released);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task RepeatedFrameUpdatesShareExactStorageAndInvalidInputRetainsPreviousImage() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        await using var control = new ImageViewerControl { ImageSource = source };
        var pixels = new byte[] { 12 };
        int released = 0;
        using var frame = ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), pixels, () => released++);
        source.SetFrame(frame);
        var first = control.ImageSourceSubmitResult;
        source.SetFrame(frame);
        Assert.True(control.ImageSourceSubmitResult!.Value.FrameId > first!.Value.FrameId);
        using (var retained = source.AcquireFrame())
        using (var lease = retained!.Acquire())
            Assert.True(lease.CpuPixels.Equals((ReadOnlyMemory<byte>)pixels));
        var invalid = Frame(); invalid.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.SetFrame(invalid));
        using (var retained = source.AcquireFrame()) Assert.Equal(12, retained!.AcquireAndRead());
        frame.Dispose(); source.Dispose();
        Assert.Equal(0, released);
        await control.DisposeAsync(); Assert.Equal(1, released);
    });

    [Fact]
    public Task ThreadBoundaryAndBackgroundPreparationAreExplicit() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        await using var control = new ImageViewerControl { ImageSource = source };
        using var frame = await Task.Run(() => Frame());
        await Task.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => source.SetFrame(frame));
            Assert.Throws<InvalidOperationException>(source.Dispose);
            Assert.Throws<InvalidOperationException>(() => control.ImageSource = source);
        });
        await ControlTestDispatcher.Run(async () =>
        {
            await using var other = new ImageViewerControl();
            Assert.Throws<InvalidOperationException>(() => other.ImageSource = source);
        });
        source.SetFrame(frame);
        Assert.Equal(FrameSubmitStatus.Accepted, control.ImageSourceSubmitResult?.Status);
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.SetFrame(frame));
        await control.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => control.ImageSource = source);
    });

    [Fact]
    public Task ReentrantReplacementCannotOverwriteNewerSourceOrReceipt() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        await using var first = new ImageViewerControl { ImageSource = source };
        await using var second = new ImageViewerControl { ImageSource = source };
        // Disposing a source-only old frame can reenter publication after both controls received the update.
        first.ImageSource = null; second.ImageSource = null;
        using (var frame = Frame(1, () => { using var latest = Frame(3); source.SetFrame(latest); })) source.SetFrame(frame);
        first.ImageSource = source; second.ImageSource = source;
        using (var frame = Frame(2)) source.SetFrame(frame);
        using (var latest = source.AcquireFrame()) Assert.Equal(3, latest!.AcquireAndRead());
        var panel = new StackPanel(); first.Height = second.Height = 100;
        panel.Children.Add(first); panel.Children.Add(second);
        var window = new Window { Content = panel, Width = 250, Height = 300, ShowActivated = false };
        try
        {
            var a = Display(first, () => { }); var b = Display(second, () => { });
            window.Show(); await Task.WhenAll(a, b);
            using var currentA = first.AcquireCurrentFrame();
            using var currentB = second.AcquireCurrentFrame();
            Assert.Equal(3, currentA!.CpuPixels.Span[0]);
            Assert.Equal(3, currentB!.CpuPixels.Span[0]);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task PipelineReleaseReentryPreservesLatestReceiptAndSurvivesSourceSwitch() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        using var replacement = new ImageBindingSource();
        using (var frame = Frame(3)) replacement.SetFrame(frame);
        await using var control = new ImageViewerControl { ImageSource = source };
        await control.SubmitFrameAsync(Frame(1, () => control.ImageSource = replacement));
        using (var frame = Frame(2)) source.SetFrame(frame);
        Assert.Same(replacement, control.ImageSource);
        Assert.Equal(3, control.ImageSourceSubmitResult?.FrameId);
    });

    [Fact]
    public Task SourceCleanupFailureIsOnceAndControlsObserveEmptySource() => ControlTestDispatcher.Run(async () =>
    {
        var source = new ImageBindingSource();
        var error = new InvalidOperationException("source release"); int releases = 0;
        using (var frame = Frame(1, () => { releases++; throw error; })) source.SetFrame(frame);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(source.Dispose));
        source.Dispose(); Assert.Equal(1, releases);
        await using var control = new ImageViewerControl { ImageSource = source };
        Assert.Null(control.ImageSourceSubmitResult);
    });

    [Fact]
    public Task FrozenReceiptAndPipelineCleanupFailureRemainObservable() => ControlTestDispatcher.Run(async () =>
    {
        using var source = new ImageBindingSource();
        var control = new ImageViewerControl { ImageSource = source };
        control.Runtime.MenuController.CaptureTarget();
        using (var frame = Frame()) source.SetFrame(frame);
        Assert.Equal(FrameSubmitStatus.Frozen, control.ImageSourceSubmitResult?.Status);
        source.SetFrame(null);
        await control.DisposeAsync();

        var error = new InvalidOperationException("pipeline release");
        var failing = new ImageViewerControl { ImageSource = source };
        using (var frame = Frame(2, () => throw error)) source.SetFrame(frame);
        source.SetFrame(null); // Only pipeline retains this frame.
        var completion = failing.DisposeAsync().AsTask();
        Assert.Contains(error, (await Assert.ThrowsAsync<AggregateException>(() => completion)).Flatten().InnerExceptions);
        Assert.Same(completion, failing.DisposeAsync().AsTask());
    });

    public sealed class Model : INotifyPropertyChanged
    {
        private ImageBindingSource? _image;
        public ImageBindingSource? Image { get => _image; set { _image = value; PropertyChanged?.Invoke(this, new(nameof(Image))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

internal static class BindingFrameTestExtensions
{
    internal static byte AcquireAndRead(this ImageFrame frame)
    {
        using var lease = frame.Acquire();
        return lease.CpuPixels.Span[0];
    }
}
