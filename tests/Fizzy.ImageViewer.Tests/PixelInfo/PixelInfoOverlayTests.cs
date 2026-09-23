using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.PixelInfo;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class PixelInfoOverlayTests
{
    private sealed class InlineRuntime : IQueryRuntime
    {
        public TimeSpan Now { get; set; }
        public IDisposable StartTicks(Action tick) => new QuerySubscription(() => { });
        public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token) => action();
        public Task PublishAsync(Action action, CancellationToken token) { action(); return Task.CompletedTask; }
    }

    [Fact]
    public async Task HudQueriesAndUnsubscribesWithoutAMeasurementContextOrViewer()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var frame = ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }).Transfer();
                var runtime = new InlineRuntime();
                using var scheduler = new PixelQueryScheduler(frame.Acquire, NullLogger.Instance, runtime);
                var image = new ImageLayer(); var hud = new HudLayer();
                using var overlay = new PixelInfoOverlay(image, hud, scheduler);
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

    private static IEnumerable<string> Text(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in Text(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
}
