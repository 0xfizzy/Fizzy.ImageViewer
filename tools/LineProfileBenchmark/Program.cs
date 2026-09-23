using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Measurements.BuiltIn;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.WriteLine("samples,mode,bytes_per_update,ms_per_update");
        foreach (int count in new[] { 1024, 4096 })
        foreach (bool gray in new[] { true, false })
        {
            var profile = new LineProfile();
            profile.Prepare(new(count, 1, count * 3, FramePixelFormat.Bgr24), 0, 0, count - 1, 0);
            var samples = new PixelSample[count];
            for (int i = 0; i < count; i++) samples[i] = new(gray ? FramePixelFormat.Gray8 : FramePixelFormat.Bgr24, i % 256, i % 256, i % 123, i % 67, 255);
            profile.Apply(samples);
            var plot = new LineProfilePlotView.LineProfilePlotControl();
            plot.Measure(new Size(600, 400));
            plot.Arrange(new Rect(0, 0, 600, 400));
            var render = (Action<DrawingContext>)typeof(LineProfilePlotView.LineProfilePlotControl)
                .GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(Action<DrawingContext>), plot);
            var visual = new DrawingVisual();
            void Update(bool draw, bool change)
            {
                if (change) profile.Red[0] = profile.Red[0] == 0 ? 1 : 0;
                plot.SetProfile(profile);
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
}
