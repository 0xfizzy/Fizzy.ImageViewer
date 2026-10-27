using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
using var stop = new CancellationTokenSource();
viewer.Closed += (_, _) => stop.Cancel();
viewer.DisplayFailed += (_, failure) => Console.Error.WriteLine($"Display failed: {failure.Error}");
using var markers = viewer.Layers.Markers.Add(Array.Empty<DrawingElement>());
viewer.Title = "Simulated stream — close the window to stop";
viewer.Show();
var pixels = new byte[640 * 480];
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
try
{
    var tick = 0;
    while (await timer.WaitForNextTickAsync(stop.Token))
    {
        Array.Fill(pixels, (byte)tick);
        // Copy detaches storage from the mutable producer buffer. Submission consumes the frame.
        var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
            new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
        if (result.Error is { } error) throw error;
        if (result.Status == FrameSubmitStatus.Closed) break;
        if (stop.IsCancellationRequested) break;
        markers.Replace(Enumerable.Range(0, 1000).Select(i =>
            new CircleElement(new Point((i * 13 + tick) % 640, i * 7 % 480), 2, Brushes.Red)));
        tick++;
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
// Production has ended before the drawing handle and Viewer are disposed.
