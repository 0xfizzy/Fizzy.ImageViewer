using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Interaction;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ImageViewportTests
{
    [Fact]
    public async Task OverlayInputReachesNavigationWithoutBecomingAnImageClick()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var root = new Grid();
                var capture = new FakeCapture();
                var viewport = new ImageViewport(root, capture);
                var overlay = new Canvas { Background = Brushes.Transparent };
                root.Children.Add(viewport);
                root.Children.Add(overlay);
                root.Measure(new Size(100, 100));
                root.Arrange(new Rect(0, 0, 100, 100));
                int imageClicks = 0, imageMoves = 0;
                viewport.ImageMouseDown += (_, _) => imageClicks++;
                viewport.ImageMouseMove += (_, _) => imageMoves++;
                overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                { RoutedEvent = Mouse.MouseWheelEvent, Handled = true });
                Assert.Equal(ViewportConstants.ScaleFactor, viewport.Scaler.ScaleX);
                overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Assert.Equal(0, imageClicks);
                overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle)
                { RoutedEvent = Mouse.MouseDownEvent });
                Assert.True(capture.IsCaptured);
                overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.MouseMoveEvent, Handled = true });
                Assert.Equal(1, imageMoves);
                overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle)
                { RoutedEvent = Mouse.MouseUpEvent });
                Assert.False(capture.IsCaptured);
                Assert.Equal(1, capture.Releases);
                completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join();
    }

    private sealed class FakeCapture : IMouseCapture
    {
        public bool IsCaptured { get; private set; }
        internal int Releases { get; private set; }
        public bool Capture() => IsCaptured = true;
        public void Release() { IsCaptured = false; Releases++; }
    }

    [Theory]
    [InlineData(1000000, 1000, 120)]
    [InlineData(1, 1000, -120)]
    public async Task FitOutsideWheelLimitsCanZoomBackTowardNormalRange(int pixels, double viewportSize, int delta)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var viewport = new ImageViewport();
                viewport.SetImage(new DrawingImage(), pixels, pixels);
                viewport.Measure(new Size(viewportSize, viewportSize));
                viewport.Arrange(new Rect(0, 0, viewportSize, viewportSize));
                viewport.FitToViewport();
                double fit = viewport.Scaler.ScaleX;
                Assert.Equal(viewportSize / pixels, fit);
                Wheel(-delta);
                Assert.Equal(fit, viewport.Scaler.ScaleX);
                Wheel(delta);
                Assert.True(delta > 0 ? viewport.Scaler.ScaleX > fit : viewport.Scaler.ScaleX < fit);
                for (int i = 0; i < 100; i++) Wheel(delta);
                Assert.InRange(viewport.Scaler.ScaleX, ViewportConstants.MinScale, ViewportConstants.MaxScale);
                completed.SetResult();

                void Wheel(int amount) => viewport.Container.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, amount)
                { RoutedEvent = Mouse.MouseWheelEvent });
            }
            catch (Exception error) { completed.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join();
    }
}
