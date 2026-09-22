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
    }
    private static double Time(Action action)
    { var timer = Stopwatch.StartNew(); action(); return timer.Elapsed.TotalMilliseconds; }
    private static void Layout(FrameworkElement root)
    { root.Measure(new(1200, 1000)); root.Arrange(new(0, 0, 1200, 1000)); root.UpdateLayout(); }
    private static void Run(int count, bool print)
    {
        var overlay = new OverlayLayer();
        var layers = new ViewerLayers(overlay, Transform.Identity);
        var data = Enumerable.Range(0, count).Select(i => (DrawingElement)new CircleElement(new(i % 100 * 10, i / 100 * 10), 3, Brushes.Red, 2, Brushes.Red)).ToArray();
        DrawingBatchHandle? batch = null;
        var create = Time(() => { batch = layers.Markers.AddBatch(data); Layout(layers.Root); });
        var replace = Time(() => { batch!.Replace(data); Layout(layers.Root); });
        var scale = Time(() => { layers.UpdateScale(2); layers.FlushScale(); Layout(layers.Root); });
        if (print) Console.WriteLine($"{count},batch,{create:F3},{replace:F3},{scale:F3},{layers.Markers.Host.Count}");
        layers.Close();

        var legacy = new OverlayLayer();
        void AddLegacy()
        {
            foreach (CircleElement circle in data)
            {
                var shape = Shapes.CreateCircle(circle.Center, circle.Radius);
                shape.Stroke = circle.Stroke; shape.Fill = circle.Fill;
                legacy.AddShape(shape);
            }
        }
        create = Time(() => { AddLegacy(); Layout(legacy); });
        replace = Time(() => { legacy.Clear(); AddLegacy(); Layout(legacy); });
        scale = Time(() => { legacy.UpdateScale(2); Layout(legacy); });
        if (print) Console.WriteLine($"{count},legacy,{create:F3},{replace:F3},{scale:F3},{legacy.Canvas.Children.Count}");
        legacy.Clear();
    }
}
