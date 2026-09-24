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
        var viewers = Enumerable.Range(0, count).Select(_ => new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false)).ToArray();
        try
        {
            foreach (var viewer in viewers)
            {
                await viewer.SubmitFrameAsync(ImageFrame.TakeOwnership(new(width, height, stride, format), data, () => { }));
                if (measure) await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Measurements.Register(new ProfileMeasurement(width, height)));
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
            results.Add(new { width, height, format = format.ToString(), fps, viewers = count, measure,
                committed = committed.Length, superseded = completed.Count(x => x.Result.Status == FrameSubmitStatus.Superseded),
                failures = completed.Count(x => x.Result.Status == FrameSubmitStatus.Failed),
                commitsPerSecond = committed.Length / elapsed.TotalSeconds,
                meanCommitMs = latency.DefaultIfEmpty().Average(), p95CommitMs = latency.Length == 0 ? 0 : latency[(int)((latency.Length - 1) * .95)],
                cpuCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / elapsed.TotalSeconds * 100,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, privateBytesDelta = process.PrivateMemorySize64 - memory,
                directWritePixelsBaselineMs = baselineMs });
            Console.WriteLine($"{width}x{height} {format} {fps}fps x{count} measurement={measure}: {committed.Length}/{completed.Length}, mean {latency.DefaultIfEmpty().Average():F2}ms");
        }
        finally { foreach (var viewer in viewers) await viewer.DisposeAsync(); }
    }
}
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/frame-benchmark.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    timestamp = DateTimeOffset.Now, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
    note = "Hidden STA windows; commit timing, not physical presentation. Baseline is direct WritePixels only, not the old full application. Allocations include harness tasks; memory deltas include caches/GC.", results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static async Task<(FrameSubmitResult, double)> Observe(ValueTask<FrameSubmitResult> task, long start)
    => (await task, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

sealed class ProfileMeasurement(int width, int height) : IFrameQueryClient
{
    public QueryRequest? Capture(FrameDescriptor d) => new LineProfileQueryRequest(new(Guid.Empty, 0), LineSampling.GetCoordinates(d,0,0,width-1,height-1), (_, samples) => { });
    public void ClearResult() { }
    public void Dispose() { }
}
