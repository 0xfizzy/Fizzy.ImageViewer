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
    {
        var hosts = await BenchmarkViewers.CreateAsync(count, embedded);
        var viewers = hosts.Viewers;
        try
        {
            foreach (var viewer in viewers)
            {
                await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(width, height, stride, format), data, () => { }));
                if (measure)
                {
                    var control = viewer is Viewer windowed ? windowed.Host.Control : (ImageViewerControl)viewer;
                    await control.Dispatcher.InvokeAsync(() => control.Runtime.Queries.Register(new ProfileMeasurement(width, height)));
                }
            }
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
                    var pending = viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(width, height, stride, format), data, () => { }));
                    outcomes.Add(Observe(pending, start));
                }
                var delay = TimeSpan.FromSeconds((i + 1.0) / fps) - timer.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
            }
            var completed = await Task.WhenAll(outcomes);
            var elapsed = timer.Elapsed;
            process.Refresh();
            var committed = completed.Where(x => x.Result.Status == FrameSubmitStatus.Committed).ToArray();
            double[] latency = committed.Select(x => x.Latency).Order().ToArray();
            results.Add(new { host = embedded ? "embedded" : "standalone", width, height, format = format.ToString(), fps, viewers = count, measure,
                committed = committed.Length, superseded = completed.Count(x => x.Result.Status == FrameSubmitStatus.Superseded),
                failures = completed.Count(x => x.Result.Status == FrameSubmitStatus.Failed),
                commitsPerSecond = committed.Length / elapsed.TotalSeconds,
                meanCommitMs = latency.DefaultIfEmpty().Average(), p95CommitMs = latency.Length == 0 ? 0 : latency[(int)((latency.Length - 1) * .95)],
                cpuCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / elapsed.TotalSeconds * 100,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, privateBytesDelta = process.PrivateMemorySize64 - memory,
                directWritePixelsBaselineMs = baselineMs });
            Console.WriteLine($"{width}x{height} {format} {fps}fps x{count} measurement={measure}: {committed.Length}/{completed.Length}, mean {latency.DefaultIfEmpty().Average():F2}ms");
        }
        finally { await hosts.DisposeAsync(); }
    }
}
var output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "artifacts/frame-benchmark.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    timestamp = DateTimeOffset.Now, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
    note = "Standalone hidden windows or embedded controls sharing one dispatcher; commit timing, not physical presentation. Baseline is direct WritePixels only, not the old full application. Allocations include harness tasks; memory deltas include caches/GC.", results
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
