using Fizzy.ImageViewer.Viewport;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ImageViewportTests
{
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
