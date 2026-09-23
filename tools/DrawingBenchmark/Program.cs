using Fizzy.ImageViewer.Measurements.Presentation;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Run(100, false); // JIT / WPF warm-up.
        Console.WriteLine("count,path,create_ms,replace_ms,scale_ms,children");
        foreach (int count in new[] { 1000, 10000 }) Run(count, true);
        Console.WriteLine("count,replace_bytes_per_update,replace_ms_per_update");
        foreach (int count in new[] { 1000, 10000 }) Allocations(count);
    }
    private static void Allocations(int count)
    {
        var layers = new ViewerLayers(Transform.Identity);
        var data = Enumerable.Range(0, count).Select(i => (DrawingElement)new CircleElement(new(i % 100 * 10, i / 100 * 10), 3, Brushes.Red, 2, Brushes.Red)).ToArray();
        var moved = data.Cast<CircleElement>().Select(c => (DrawingElement)(c with { Center = c.Center + new Vector(1, 1) })).ToArray();
        using var batch = layers.Markers.AddBatch(data);
        for (int i = 0; i < 10; i++) batch.Replace(i % 2 == 0 ? moved : data);
        const int iterations = 50;
        long start = GC.GetAllocatedBytesForCurrentThread();
        long ticks = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) batch.Replace(i % 2 == 0 ? moved : data);
        var elapsed = Stopwatch.GetElapsedTime(ticks);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Console.WriteLine($"{count},{bytes / iterations},{elapsed.TotalMilliseconds / iterations:F3}");
        layers.Close();
    }
    private static double Time(Action action)
    { var timer = Stopwatch.StartNew(); action(); return timer.Elapsed.TotalMilliseconds; }
    private static void Layout(FrameworkElement root)
    { root.Measure(new(1200, 1000)); root.Arrange(new(0, 0, 1200, 1000)); root.UpdateLayout(); }
    private static void Run(int count, bool print)
    {
        var layers = new ViewerLayers(Transform.Identity);
        var data = Enumerable.Range(0, count).Select(i => (DrawingElement)new CircleElement(new(i % 100 * 10, i / 100 * 10), 3, Brushes.Red, 2, Brushes.Red)).ToArray();
        DrawingBatchHandle? batch = null;
        var create = Time(() => { batch = layers.Markers.AddBatch(data); Layout(layers.Root); });
        var replace = Time(() => { batch!.Replace(data); Layout(layers.Root); });
        var scale = Time(() => { layers.UpdateScale(2); layers.FlushScale(); Layout(layers.Root); });
        if (print) Console.WriteLine($"{count},batch,{create:F3},{replace:F3},{scale:F3},{layers.Markers.Host.Count}");
        layers.Close();

        var legacyLayers = new ViewerLayers(Transform.Identity);
        var legacy = legacyLayers.Measurements.Overlay;
        void AddLegacy()
        {
            foreach (CircleElement circle in data)
            {
                var shape = MeasurementVisualFactory.CreateCircle(circle.Center, circle.Radius);
                shape.Stroke = circle.Stroke; shape.Fill = circle.Fill;
                legacy.AddShape(shape);
            }
        }
        create = Time(() => { AddLegacy(); Layout(legacy); });
        replace = Time(() => { legacy.ClearVisuals(); AddLegacy(); Layout(legacy); });
        scale = Time(() => { legacy.UpdateScale(2); Layout(legacy); });
        if (print) Console.WriteLine($"{count},legacy,{create:F3},{replace:F3},{scale:F3},{legacy.Canvas.Children.Count}");
        legacyLayers.Close();
    }
}
