using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods;

public class LineStrengthMeasure : LineMeasure
{
    public override string Id => "LineStrength";
    public override string DisplayName => "Line strength";
    private protected override MeasurementItem CreateItem(MeasureContext context, Point start) => new LineStrengthItem(context, start);

    private sealed class LineStrengthItem : MeasurementItem
    {
        private static readonly ScottPlot.Color ScottRed = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
        private static readonly ScottPlot.Color ScottGreen = ScottPlot.Color.FromColor(System.Drawing.Color.Green);
        private static readonly ScottPlot.Color ScottBlue = ScottPlot.Color.FromColor(System.Drawing.Color.Blue);
        private QueryRequest? _cached;
        private (long Version, Frames.FrameDescriptor Descriptor)? _cachedGeometry;
        private Imaging.LineProfile _profile = new();
        private double[] _xs = [], _rs = [], _gs = [], _bs = [];
        private ScottPlot.Plottables.Scatter? _red, _green, _blue;
        public LineStrengthItem(MeasureContext context, Point start)
            : base(context, MeasurementGeometry.Line(start, start), Shapes.CreateLine(), Shapes.CreateLabel(start, "", 5, 0)) { }
        public override void Complete()
        {
            base.Complete();
            var window = new Window { Title = "Pixel Values", Width = 600, Height = 400, Topmost = true, Content = new ScottPlot.WPF.WpfPlot() };
            OwnWindow(window);
            window.Show();
            Subscribe();
        }
        public override QueryRequest? Capture(Frames.FrameDescriptor descriptor)
        {
            if (IsDisposed || !IsComplete) return null;
            var geometry = (Geometry.Version, descriptor);
            if (_cachedGeometry == geometry) return _cached;
            _cachedGeometry = geometry;
            var profile = new Imaging.LineProfile();
            var points = profile.Prepare(descriptor, Geometry.Start.X, Geometry.Start.Y, Geometry.End.X, Geometry.End.Y);
            return _cached = new LineProfileQueryRequest(new(Id, Geometry.Version), points, samples =>
            { profile.Apply(samples); _profile = profile; Result = profile; Publish(); });
        }
        public override void ClearResult()
        {
            base.ClearResult();
            if (ResultWindow?.Content is ScottPlot.WPF.WpfPlot plot)
            {
                if (_red != null) _red.IsVisible = false;
                if (_green != null) _green.IsVisible = false;
                if (_blue != null) _blue.IsVisible = false;
                plot.Refresh();
            }
        }
        private void Publish()
        {
            if (IsDisposed || ResultWindow == null) return;
            var plot = (ScottPlot.WPF.WpfPlot)ResultWindow.Content;
            int count = _profile.Count;
            if (count > _xs.Length)
            {
                _xs = new double[count]; _rs = new double[count]; _gs = new double[count]; _bs = new double[count];
                plot.Plot.Clear();
                _red = plot.Plot.Add.Scatter(_xs, _rs, ScottRed);
                _green = plot.Plot.Add.Scatter(_xs, _gs, ScottGreen);
                _blue = plot.Plot.Add.Scatter(_xs, _bs, ScottBlue);
            }
            if (_red != null)
            {
                Array.Copy(_profile.Distances, _xs, count); Array.Copy(_profile.Red, _rs, count);
                Array.Copy(_profile.Green, _gs, count); Array.Copy(_profile.Blue, _bs, count);
                // Non-finite raw samples remain available through the reader; plots use gaps.
                for (int i = 0; i < count; i++)
                {
                    if (!double.IsFinite(_rs[i])) _rs[i] = double.NaN;
                    if (!double.IsFinite(_gs[i])) _gs[i] = double.NaN;
                    if (!double.IsFinite(_bs[i])) _bs[i] = double.NaN;
                }
                _red.IsVisible = count > 0;
                _green!.IsVisible = _blue!.IsVisible = count > 0 && !_profile.IsGray;
                if (count > 0)
                {
                    _red.Data.MaxRenderIndex = _green.Data.MaxRenderIndex = _blue.Data.MaxRenderIndex = count - 1;
                    plot.Plot.Axes.AutoScale();
                }
            }
            ResultWindow.Title = "Pixel Values";
            plot.Refresh();
        }
    }
}
