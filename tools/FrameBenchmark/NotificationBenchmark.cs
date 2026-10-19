using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;

internal static class NotificationBenchmark
{
    internal static async Task RunAsync(string output)
    {
        const int iterations = 1_000_000;
        var results = new List<object>();
        foreach (bool embedded in new[] { false, true })
        foreach (int subscribers in new[] { 0, 1, 4 })
        {
            await using var host = await BenchmarkViewers.CreateAsync(1, embedded);
            var viewer = host.Viewers[0];
            var control = viewer is Viewer standalone ? standalone.Host.Control : (ImageViewerControl)viewer;
            long observed = 0;
            for (int i = 0; i < subscribers; i++) viewer.FrameCommitted += (_, info) => observed += info.FrameId;
            var info = new FrameInfo(1, new(1, 1, 1, FramePixelFormat.Gray8), null);
            await control.Dispatcher.InvokeAsync(() =>
            {
                // Include the control-to-Viewer forwarding path in standalone measurements.
                for (int i = 0; i < 100_000; i++) control.NotifyFrameCommitted(info);
                for (int sample = 0; sample < 5; sample++)
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++) control.NotifyFrameCommitted(info);
                    var elapsed = Stopwatch.GetElapsedTime(start);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    results.Add(new { host = embedded ? "embedded" : "standalone", subscribers, sample,
                        nanosecondsPerNotification = elapsed.TotalNanoseconds / iterations,
                        bytesPerNotification = (double)bytes / iterations });
                }
            });
            if (observed != (long)subscribers * (100_000 + 5 * iterations))
                throw new InvalidOperationException("Notification delivery count changed.");
        }
        foreach (int subscribers in new[] { 0, 1, 4 })
        {
            var queue = new MeasurementNotificationQueue(NullLogger.Instance);
            var geometry = MeasurementGeometry.Point(new(1, 0));
            object sender = new();
            long observed = 0;
            EventHandler<MeasurementGeometry>? handlers = null;
            for (int i = 0; i < subscribers; i++) handlers += (_, value) => observed += (long)value.Anchor.X;
            int warmupIterations = 0;
            var warmup = Stopwatch.StartNew();
            while (warmup.ElapsedMilliseconds < 1000)
            {
                queue.Notify(handlers, sender, geometry);
                warmupIterations++;
            }
            for (int sample = 0; sample < 5; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) queue.Notify(handlers, sender, geometry);
                var elapsed = Stopwatch.GetElapsedTime(start);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                results.Add(new { host = "measurement-queue", subscribers, sample,
                    nanosecondsPerNotification = elapsed.TotalNanoseconds / iterations,
                    bytesPerNotification = (double)bytes / iterations });
            }
            if (observed != (long)subscribers * (warmupIterations + 5 * iterations))
                throw new InvalidOperationException("Measurement notification delivery count changed.");
        }
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(), iterations, results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
