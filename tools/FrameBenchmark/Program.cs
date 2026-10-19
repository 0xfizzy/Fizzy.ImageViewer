using Fizzy.ImageViewer.Imaging.Queries;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

if (args.FirstOrDefault() == "--notifications")
{
    await NotificationBenchmark.RunAsync(args.Skip(1).FirstOrDefault() ?? "artifacts/notification-benchmark.json");
    return;
}

if (args.FirstOrDefault() == "--snapshots")
{
    await SnapshotCopyBenchmark.RunAsync(args.Skip(1).FirstOrDefault() ?? "artifacts/snapshot-copy-benchmark.json");
    return;
}

bool embedded = args.Contains("--embedded");
var results = new List<object>();
foreach (var (width, height) in new[] { (1920, 1080), (3840, 2160) })
foreach (var format in new[] { FramePixelFormat.Gray8, FramePixelFormat.Bgr24 })
{
    int stride = width * format.BytesPerPixel();
    var data = new byte[stride * height]; new Random(42).NextBytes(data);
    await using var baselineHost = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    WriteableBitmap? baseline = null;
    await baselineHost.Host.Window.Dispatcher.InvokeAsync(() => baseline = new WriteableBitmap(width, height, 96, 96,
        format == FramePixelFormat.Gray8 ? PixelFormats.Gray8 : PixelFormats.Bgr24, null));
    for (int i = 0; i < 3; i++) await baselineHost.Host.Window.Dispatcher.InvokeAsync(() => baseline!.WritePixels(new Int32Rect(0, 0, width, height), data, stride, 0));
    var timer = Stopwatch.StartNew();
    for (int i = 0; i < 20; i++) await baselineHost.Host.Window.Dispatcher.InvokeAsync(() => baseline!.WritePixels(new Int32Rect(0, 0, width, height), data, stride, 0));
    double baselineMs = timer.Elapsed.TotalMilliseconds / 20;
    foreach (int fps in new[] { 30, 60 })
    foreach (int count in new[] { 1, 2 })
    foreach (bool measure in new[] { false, true })
    foreach (bool hidden in new[] { false, true })
    {
        var hosts = await BenchmarkViewers.CreateAsync(count, embedded);
        var viewers = hosts.Viewers;
        try
        {
            foreach (var viewer in viewers)
            {
                var warm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(width, height, stride, format), data, () => { }),
                    new() { OnCommitted = _ => warm.TrySetResult() });
                await warm.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (measure)
                {
                    var control = viewer is Viewer windowed ? windowed.Host.Control : (ImageViewerControl)viewer;
                    await control.Dispatcher.InvokeAsync(() => control.Runtime.Queries.Register(new ProfileMeasurement(width, height)));
                }
            }
            if (hidden) await hosts.SetVisibleAsync(false);
            var displayLatency = new System.Collections.Concurrent.ConcurrentBag<double>();
            int failures = 0;
            foreach (var viewer in viewers) viewer.DisplayFailed += (_, _) => Interlocked.Increment(ref failures);
            using var process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true), memory = process.PrivateMemorySize64;
            var cpu = process.TotalProcessorTime;
            timer.Restart();
            var outcomes = new List<Task<(FrameSubmitResult Result, double Latency)>>();
            for (int i = 0; i < fps; i++)
            {
                foreach (var viewer in viewers)
                {
                    long start = Stopwatch.GetTimestamp();
                    var pending = viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(width, height, stride, format), data, () => { }),
                        new() { OnCommitted = _ => displayLatency.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds) });
                    outcomes.Add(Observe(pending, start));
                }
                var delay = TimeSpan.FromSeconds((i + 1.0) / fps) - timer.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
            }
            var completed = await Task.WhenAll(outcomes);
            var elapsed = timer.Elapsed;
            process.Refresh();
            int hiddenCommits = displayLatency.Count;
            double resumeMs = 0;
            if (hidden)
            {
                var resumed = viewers.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
                for (int index = 0; index < viewers.Length; index++)
                {
                    var signal = resumed[index];
                    viewers[index].FrameCommitted += (_, _) => signal.TrySetResult();
                }
                var resume = Stopwatch.StartNew();
                await hosts.SetVisibleAsync(true);
                await Task.WhenAll(resumed.Select(x => x.Task)).WaitAsync(TimeSpan.FromSeconds(10));
                resumeMs = resume.Elapsed.TotalMilliseconds;
            }
            foreach (var viewer in viewers)
            {
                var control = viewer is Viewer standalone ? standalone.Host.Control : (ImageViewerControl)viewer;
                await control.Runtime.Pipeline.Completion;
            }
            double[] latency = displayLatency.Order().ToArray();
            int committed = latency.Length;
            results.Add(new { host = embedded ? "embedded" : "standalone", width, height, format = format.ToString(), fps, viewers = count, measure, hidden,
                accepted = completed.Count(x => x.Result.Status == FrameSubmitStatus.Accepted), committed,
                hiddenCommits = hidden ? hiddenCommits : (int?)null, failures, resumeMs,
                commitsPerSecond = hidden ? 0 : committed / elapsed.TotalSeconds,
                meanReceptionMs = completed.Select(x => x.Latency).DefaultIfEmpty().Average(),
                meanCommitMs = latency.DefaultIfEmpty().Average(), p95CommitMs = latency.Length == 0 ? 0 : latency[(int)((latency.Length - 1) * .95)],
                cpuCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / elapsed.TotalSeconds * 100,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, privateBytesDelta = process.PrivateMemorySize64 - memory,
                directWritePixelsBaselineMs = baselineMs });
            if (hidden && hiddenCommits != 0) throw new InvalidOperationException("Hidden viewers committed frames.");
            Console.WriteLine($"{width}x{height} {format} {fps}fps x{count} measurement={measure} hidden={hidden}: displayed {committed}/{completed.Length}, resume {resumeMs:F2}ms");
        }
        finally { await hosts.DisposeAsync(); }
    }
}
var output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "artifacts/frame-benchmark.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    timestamp = DateTimeOffset.Now, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
    note = "Shown standalone windows or hosted embedded controls, plus hidden/resume cases. Reception and commit timing are separate; neither measures physical presentation. Baseline is direct WritePixels only, not the old full application. Allocations include harness tasks; memory deltas include caches/GC.", results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static async Task<(FrameSubmitResult, double)> Observe(ValueTask<FrameSubmitResult> task, long start)
    => (await task, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

sealed class ProfileMeasurement(int width, int height) : IFrameQueryClient
{
    public QueryRequest? Capture(FrameDescriptor descriptor) => new CoordinateQueryRequest(QueryRateCategory.Line, new(Guid.Empty, 0), LineSampling.GetCoordinates(descriptor,0,0,width-1,height-1), (_, samples) => { });
    public void ClearResult() { }
    public void Dispose() { }
}
