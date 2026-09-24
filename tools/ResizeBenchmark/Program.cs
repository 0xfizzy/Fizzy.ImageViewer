using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

bool stationary = args.Contains("--stationary");
bool widthOnly = args.Contains("--width-only");
var cases = new[] {
    new Case("empty", 0, 0, false, false),
    new Case("static-4k", 3840, 0, false, false),
    new Case("static-4k-1000", 3840, 1000, false, false),
    new Case("static-4k-10000", 3840, 10000, false, false),
    new Case("static-4k-10000-no-scale", 3840, 10000, false, false, true),
    new Case("stream-1080p", 1920, 0, true, false),
    new Case("stream-4k", 3840, 0, true, false),
    new Case("static-4k-update-10000", 3840, 10000, false, true),
    new Case("stream-4k-update-10000", 3840, 10000, true, true)
};
if (args.Contains("--control")) cases = cases.Where(c => c.Name is "empty" or "static-4k" or "stream-4k" or "stream-4k-update-10000").ToArray();
var results = new List<object>();
int tier = 0;
foreach (int repeat in Enumerable.Range(1, 2))
foreach (var c in repeat == 1 ? cases : cases.Reverse())
{
    var presenter = new TimedPresenter();
    await using var viewer = new Viewer(NullLogger<Viewer>.Instance, presenter, true, 80, 80, 900, 650);
    var dispatcher = viewer.Host.Window.Dispatcher;
    var window = viewer.Host.Window;
    int height = c.Width * 9 / 16;
    var data = new byte[c.Width * height * 3];
    new Random(42).NextBytes(data);
    ImageFrame Frame() => ImageFrame.TakeOwnership(new(c.Width, height, c.Width * 3, FramePixelFormat.Bgr24), data, () => { });
    if (c.Width > 0) await viewer.SubmitFrameAsync(Frame());
    var elements = Enumerable.Range(0, c.Count).Select(i => (DrawingElement)new CircleElement(
        new(i % 125 * 28, i / 125 * 24), 5, Brushes.Red, 2) {
        ScaleMode = c.NoScale ? OverlayScaleMode.ScaleWithImage : OverlayScaleMode.FixedStroke }).ToArray();
    var moved = elements.Cast<CircleElement>().Select(e => (DrawingElement)(e with { Center = e.Center + new Vector(3, 3) })).ToArray();
    using var batch = c.Count > 0 ? viewer.Layers.Markers.Add(elements) : null;
    await Task.Delay(400);
    var renderGaps = new List<double>();
    double lastRender = -1;
    var watch = Stopwatch.StartNew();
    EventHandler rendering = (_, e) => {
        double time = ((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;
        if (lastRender >= 0 && time > lastRender) renderGaps.Add(time - lastRender);
        lastRender = time;
    };
    await dispatcher.InvokeAsync(() => { tier = RenderCapability.Tier >> 16; CompositionTarget.Rendering += rendering; });
    presenter.Samples.Clear();
    var inputLatency = new List<double>();
    var resizeWork = new List<double>();
    var replaceWork = new List<double>();
    int committed = 0, superseded = 0, failed = 0;
    using var process = Process.GetCurrentProcess();
    var cpu = process.TotalProcessorTime;
    long allocated = GC.GetTotalAllocatedBytes();
    async Task Pace(int n) {
        var delay = TimeSpan.FromSeconds(n / 60.0) - watch.Elapsed;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
    }
    watch.Restart();
    var stream = Task.Run(async () => {
        if (!c.Stream) return;
        int n = 0;
        while (watch.Elapsed.TotalSeconds < 3) {
            var result = await viewer.SubmitFrameAsync(Frame());
            if (result.Status == FrameSubmitStatus.Committed) committed++;
            else if (result.Status == FrameSubmitStatus.Superseded) superseded++;
            else failed++;
            await Pace(++n);
        }
    });
    var update = Task.Run(async () => {
        if (!c.Update) return;
        int n = 0;
        while (watch.Elapsed.TotalSeconds < 3) {
            long start = Stopwatch.GetTimestamp();
            batch!.Replace(n % 2 == 0 ? elements : moved);
            replaceWork.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            await Pace(++n);
        }
    });
    int samples = 0;
    while (watch.Elapsed.TotalSeconds < 3) {
        long queued = Stopwatch.GetTimestamp();
        await dispatcher.InvokeAsync(() => {
            inputLatency.Add(Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
            long start = Stopwatch.GetTimestamp();
            double phase = watch.Elapsed.TotalSeconds * Math.PI * 2 / 1.5;
            if (!stationary) {
                window.Width = 1000 + 180 * Math.Sin(phase);
                if (!widthOnly) window.Height = 750 + 130 * Math.Sin(phase);
            }
            resizeWork.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }, DispatcherPriority.Input);
        await Pace(++samples);
    }
    await Task.WhenAll(stream, update);
    await dispatcher.InvokeAsync(() => CompositionTarget.Rendering -= rendering);
    var resultRow = new { scenario = c.Name, repeat, stationary, widthOnly, tier, samples, committed, superseded, failed,
        inputMs = Stats(inputLatency), resizeSetterMs = Stats(resizeWork), presentMs = Stats(presenter.Samples),
        replaceMs = Stats(replaceWork), renderingGapMs = Stats(renderGaps),
        cpuCorePercent = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100,
        allocatedBytes = GC.GetTotalAllocatedBytes() - allocated };
    results.Add(resultRow);
    Console.WriteLine(JsonSerializer.Serialize(resultRow));
}
string output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/resize-benchmark.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now,
    runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
    note = "Visible WPF windows, programmatic sinusoidal resize at up to 60 Hz, 3 seconds per case, two reverse-order passes. Input measures Dispatcher Input queue delay; resizeSetter excludes deferred layout/render. Rendering events are not physical presentation. Stream awaits each commit, immutable Bgr24 buffer reused. Continuous Rendering subscription adds common baseline overhead. No native mouse resize modal loop or D3D producer tested.", results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static object Stats(IEnumerable<double> values) {
    var a = values.Order().ToArray();
    return new { count = a.Length, mean = a.Length == 0 ? 0 : a.Average(),
        p95 = a.Length == 0 ? 0 : a[(int)((a.Length - 1) * .95)], max = a.Length == 0 ? 0 : a[^1],
        over33 = a.Count(x => x > 33.4) };
}
record Case(string Name, int Width, int Count, bool Stream, bool Update, bool NoScale = false);
sealed class TimedPresenter : ICpuImagePresenter {
    readonly WriteableBitmapPresenter inner = new();
    public ConcurrentQueue<double> Samples { get; } = new();
    public ImageSource Present(DisplayBuffer pixels) {
        long start = Stopwatch.GetTimestamp();
        var source = inner.Present(pixels);
        Samples.Enqueue(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return source;
    }
    public void Dispose() => inner.Dispose();
}
