using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Hud;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class PixelInfoControllerTests
{
    private sealed class InlineRuntime : IQueryRuntime
    {
        public TimeSpan Now { get; set; }
        public IDisposable StartTicks(Action tick) => new QuerySubscription(() => { });
        public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token) => action();
        public Task PublishAsync(Action action, CancellationToken token) { action(); return Task.CompletedTask; }
    }

    [Fact]
    public async Task HudQueriesAndUnsubscribesWithoutAMeasurementCollectionOrViewer()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var frame = ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }).Transfer();
                var runtime = new InlineRuntime();
                using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
                var image = new ImageViewport(); var hud = new HudLayer();
                using var overlay = new PixelInfoController(image, hud, scheduler, () => new Point(0, 0));
                overlay.Enable(); overlay.Enable();
                image.Container.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
                scheduler.Tick();
                Assert.True(scheduler.Completion.IsCompletedSuccessfully);
                Assert.Equal(1, scheduler.QueryMetrics.Batches);
                Assert.Contains(Text((DependencyObject)hud.Content), value => value.Contains("GRAY:") && value.Contains("42"));
                overlay.Disable(); overlay.Disable();
                runtime.Now = TimeSpan.FromSeconds(1);
                image.Container.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
                scheduler.Tick();
                Assert.Equal(1, scheduler.QueryMetrics.Batches);
                Assert.False(overlay.IsEnabled);
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join();
    }

    [Fact]
    public async Task StationaryPointerUsesCurrentFitLayoutAndSession()
    {
        await OnSta(() =>
        {
            var descriptor = new FrameDescriptor(100, 100, 100, FramePixelFormat.Gray8);
            using var scheduler = new PixelQueryScheduler(() => null, NullLogger.Instance, new InlineRuntime());
            var image = new ImageViewport();
            SetImage(100);
            image.Measure(new Size(100, 100));
            image.Arrange(new Rect(0, 0, 100, 100));
            image.UpdateLayout();
            image.FitToViewport();
            Point? pointer = new Point(40, 40);
            using var controller = new PixelInfoController(image, new HudLayer(), scheduler, () => pointer);
            var client = (IFrameQueryClient)controller;
            controller.Enable();
            var initial = Capture(40, 40);

            image.Scaler.ScaleX = image.Scaler.ScaleY = 2;
            Capture(20, 20);
            image.FitToViewport();
            Capture(40, 40);

            image.Measure(new Size(200, 200));
            image.Arrange(new Rect(0, 0, 200, 200));
            image.UpdateLayout();
            image.FitToViewport();
            Capture(20, 20);

            // The current pointer reader also accounts for container movement in layout.
            pointer = new Point(60, 40);
            Capture(30, 20);
            pointer = null;
            Assert.Null(client.Capture(descriptor));
            pointer = new Point(60, 40);
            var returned = Capture(30, 20);
            Assert.NotEqual(initial.Identity.SessionVersion, returned.Identity.SessionVersion);

            controller.Disable();
            Assert.Null(client.Capture(descriptor));
            pointer = new Point(80, 40);
            controller.Enable();
            Capture(40, 20);

            SetImage(50);
            image.UpdateLayout();
            descriptor = new FrameDescriptor(50, 50, 50, FramePixelFormat.Gray8);
            image.FitToViewport();
            Capture(20, 10);

            // Growing the frame must accept a newly covered pixel on the first capture,
            // even though it lies outside the previously captured descriptor.
            pointer = new Point(150, 150);
            SetImage(100);
            image.UpdateLayout();
            descriptor = new FrameDescriptor(100, 100, 100, FramePixelFormat.Gray8);
            image.FitToViewport();
            Capture(75, 75);

            void SetImage(int size) => image.SetImage(
                BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, new byte[size * size], size),
                size, size);

            CoordinateQueryRequest Capture(int x, int y)
            {
                var request = Assert.IsType<CoordinateQueryRequest>(client.Capture(descriptor));
                Assert.Equal(new PixelCoordinate(x, y), Assert.Single(request.Coordinates));
                return request;
            }
        });
    }

    [Fact]
    public async Task LeavingImageForSiblingOverlayDoesNotInvalidateHudSession()
    {
        await OnSta(() =>
        {
            var root = new Grid();
            var image = new ImageViewport(root);
            var overlay = new Canvas();
            root.Children.Add(image);
            root.Children.Add(overlay);
            root.Measure(new Size(100, 100));
            root.Arrange(new Rect(0, 0, 100, 100));
            using var scheduler = new PixelQueryScheduler(() => null, NullLogger.Instance, new InlineRuntime());
            using var controller = new PixelInfoController(image, new HudLayer(), scheduler, () => new Point(5, 5));
            controller.Enable();
            var client = (IFrameQueryClient)controller;
            var descriptor = new FrameDescriptor(10, 10, 10, FramePixelFormat.Gray8);
            var before = Assert.IsType<CoordinateQueryRequest>(client.Capture(descriptor));
            image.Container.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            var overOverlay = Assert.IsType<CoordinateQueryRequest>(client.Capture(descriptor));
            Assert.Equal(before.Identity.SessionVersion, overOverlay.Identity.SessionVersion);
            root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            var reentered = Assert.IsType<CoordinateQueryRequest>(client.Capture(descriptor));
            Assert.NotEqual(before.Identity.SessionVersion, reentered.Identity.SessionVersion);
        });
    }

    [Fact]
    public async Task UnloadedViewportDoesNotQueryEvenAfterSyntheticMouseMove()
    {
        await OnSta(() =>
        {
            using var frame = ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }).Transfer();
            using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, new InlineRuntime());
            var image = new ImageViewport();
            using var controller = new PixelInfoController(image, new HudLayer(), scheduler);
            controller.Enable();
            image.Container.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            scheduler.Tick();
            Assert.Equal(0, scheduler.QueryMetrics.Batches);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ClearingHudLabelReadsBackEmptyString(string? cleared)
    {
        await OnSta(() =>
        {
            var hud = new HudLayer { Label = "old" };
            hud.Label = cleared;
            Assert.Equal(string.Empty, hud.Label);
            Assert.DoesNotContain("old", Text((DependencyObject)hud.Content));
            hud.Label = "new";
            Assert.Equal("new", hud.Label);
        });
    }

    private static async Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join();
    }

    private static IEnumerable<string> Text(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Text(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
}
