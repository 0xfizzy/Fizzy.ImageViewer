using Fizzy.ImageViewer.Measurements.Presentation;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Imaging.Queries;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--query-publication"))
        {
            MeasureQueryPublication();
            return;
        }
        Console.WriteLine("samples,mode,bytes_per_update,ms_per_update");
        foreach (int count in new[] { 1024, 4096 })
        foreach (bool gray in new[] { true, false })
        {
            var samples = new PixelSample[count];
            for (int i = 0; i < count; i++) samples[i] = new(gray ? FramePixelFormat.Gray8 : FramePixelFormat.Bgr24, i % 256, i % 256, i % 123, i % 67, 255);
            var plot = new LineProfileWindow.LineProfilePlotControl();
            plot.Measure(new Size(600, 400));
            plot.Arrange(new Rect(0, 0, 600, 400));
            var render = (Action<DrawingContext>)typeof(LineProfileWindow.LineProfilePlotControl)
                .GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<DrawingContext>), plot);
            var visual = new DrawingVisual();
            void Update(bool draw, bool change)
            {
                if (change) samples[0] = samples[0] with { Gray = samples[0].Gray == 0 ? 1 : 0, R = samples[0].R == 0 ? 1 : 0 };
                plot.SetProfile(samples);
                if (draw) { using var dc = visual.RenderOpen(); render(dc); }
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                if (gray) continue;
                Update(true, false);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = System.IO.File.Create(args[1]);
                encoder.Save(output);
                return;
            }
            foreach (var mode in new[] { "copy", "changed-render", "unchanged-render", "raster" })
            {
                bool draw = mode != "copy", change = mode != "unchanged-render";
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32);
                for (int i = 0; i < 50; i++) { Update(draw, change); if (mode == "raster") bitmap.Render(visual); }
                int iterations = mode == "raster" ? 50 : 500;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) { Update(draw, change); if (mode == "raster") bitmap.Render(visual); }
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                Console.WriteLine($"{count},{(gray ? "gray" : "rgb")}-{mode},{bytes / iterations},{elapsed.TotalMilliseconds / iterations:F4}");
            }
        }
    }

    private static void MeasureQueryPublication()
    {
        Console.WriteLine("# Cached line request publication only; excludes provider reads, request construction and STA dispatch.");
        Console.WriteLine("samples,mode,bytes_per_publication,ms_per_publication");
        foreach (int count in new[] { 1024, 4096 })
        {
            var descriptor = new FrameDescriptor(count, 1, count, FramePixelFormat.Gray8);
            var frame = new FrameInfo(1, descriptor, null);
            var identity = new QueryIdentity(Guid.NewGuid(), 0);
            var samples = new PixelSample[count];
            MeasurementSampleResult? retained = null;
            var request = (CoordinateQueryRequest)MeasurementQueryDefinition.CreateRequest(
                MeasurementQueryKind.LineProfile, MeasurementGeometry.Line(new(0.5, 0.5), new(count - 0.5, 0.5)),
                descriptor, identity, result => retained = (MeasurementSampleResult)result)!;
            void Current() => request.Publish(frame, samples);
            // Reproduce the previous publication's ToArray followed by the defensive
            // constructor's sample clone and per-result coordinate clone.
            void Previous() => retained = new MeasurementSampleResult(identity.ClientId, identity.GeometryVersion,
                frame, MeasurementQueryKind.LineProfile, request.Coordinates, samples.AsSpan().ToArray());
            foreach (var (name, publish) in new (string, Action)[] { ("previous-copies", Previous), ("current-request", Current) })
            {
                for (int i = 0; i < 100; i++) publish();
                const int iterations = 2000;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) publish();
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                GC.KeepAlive(retained);
                Console.WriteLine($"{count},{name},{bytes / iterations},{elapsed.TotalMilliseconds / iterations:F6}");
            }
        }
    }
}
