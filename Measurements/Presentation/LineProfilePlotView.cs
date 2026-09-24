using Fizzy.ImageViewer.Frames;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Measurements.Presentation;

/// <summary>UI-thread presentation; the measurement owns the window lifetime.</summary>
internal sealed class LineProfilePlotView
{
    private readonly LineProfilePlotControl _plot = new();
    public Window Window { get; } = new() { Title = "Pixel Values", Width = 600, Height = 400, Topmost = true };
    public LineProfilePlotView() => Window.Content = _plot;
    public void Clear() => _plot.Clear();
    public void ShowProfile(IReadOnlyList<PixelSample> samples) => _plot.SetProfile(samples);

    internal sealed class LineProfilePlotControl : FrameworkElement
    {
        private static readonly Pen GridPen = CreateGridPen(224);
        private static readonly Pen MinorGridPen = CreateGridPen(241);
        private static readonly Pen AxisPen = CreatePen(Color.FromRgb(110, 120, 130), 1);
        private DrawingGroup? _axes;
        private double _axisMin, _axisMax, _axisXMax, _axisDpi;
        private Size _axisSize;
        private static readonly Pen[] CurvePens = [CreatePen(Color.FromRgb(211, 68, 65), 1.25), CreatePen(Color.FromRgb(43, 143, 79), 1.25), CreatePen(Color.FromRgb(54, 108, 207), 1.25)];
        private readonly double[][] _values = [[], [], []];
        private readonly StreamGeometry?[] _curves = new StreamGeometry?[3];
        private readonly List<Point> _points = [];
        private int _count;
        private bool _gray, _dirty = true;
        private Size _geometrySize;
        private double _geometryDpi;

        internal int SampleCount => _count;
        internal int ChannelCount => _count == 0 ? 0 : _gray ? 1 : 3;
        internal ReadOnlyMemory<double> GetSamples(int channel) => _values[channel].AsMemory(0, _count);
        public LineProfilePlotControl() => SnapsToDevicePixels = true;

        private static Pen CreatePen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness)
            { LineJoin = PenLineJoin.Round };
            pen.Freeze(); // Safe to share across independent viewer STAs.
            return pen;
        }

        private static Pen CreateGridPen(byte shade)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(shade, shade, shade)), 1)
            { DashStyle = new DashStyle([3, 3], 0) };
            pen.Freeze();
            return pen;
        }

        private static double TickStep(double range, int divisions)
        {
            double raw = range / divisions;
            double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double fraction = raw / power;
            return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        }

        private void DrawAxes(DrawingContext dc, double min, double max, double xMax,
            double left, double top, double pw, double ph, double dpi)
        {
            if (_axes == null || _axisSize != RenderSize || _axisMin != min || _axisMax != max ||
                _axisXMax != xMax || _axisDpi != dpi)
            {
                var axes = new DrawingGroup();
                using (var drawing = axes.Open())
                {
                    var typeface = new Typeface("Segoe UI");
                    FormattedText Text(string value) => new(value, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, 11, Brushes.DimGray, dpi);
                    double xStep = Math.Max(1, TickStep(xMax, Math.Clamp((int)(pw / 65), 2, 12)));
                    double yStep = TickStep(max - min, Math.Clamp((int)(ph / 45), 2, 12));
                    for (int i = 0; i <= 100; i++)
                    {
                        double value = i * xStep / 2;
                        if (value > xMax) break;
                        double x = left + pw * value / xMax;
                        drawing.DrawLine(i % 2 == 0 ? GridPen : MinorGridPen, new(x, top), new(x, top + ph));
                        if (i % 2 == 0)
                        {
                            var label = Text(value.ToString("G4", CultureInfo.InvariantCulture));
                            drawing.DrawText(label, new(x - label.Width / 2, top + ph + 7));
                        }
                    }
                    double first = Math.Ceiling(min / (yStep / 2));
                    for (int i = 0; i <= 100; i++)
                    {
                        double tick = first + i, value = tick * yStep / 2;
                        if (value > max) break;
                        double y = top + ph * (1 - (value - min) / (max - min));
                        drawing.DrawLine(tick % 2 == 0 ? GridPen : MinorGridPen, new(left, y), new(left + pw, y));
                        if (tick % 2 == 0)
                        {
                            var label = Text(value.ToString("G4", CultureInfo.InvariantCulture));
                            drawing.DrawText(label, new(left - label.Width - 8, y - label.Height / 2));
                        }
                    }
                    drawing.DrawLine(AxisPen, new(left, top), new(left, top + ph));
                    drawing.DrawLine(AxisPen, new(left, top + ph), new(left + pw, top + ph));
                }
                axes.Freeze();
                _axes = axes;
                _axisSize = RenderSize;
                _axisMin = min; _axisMax = max; _axisXMax = xMax; _axisDpi = dpi;
            }
            dc.DrawDrawing(_axes);
        }

        public void Clear()
        {
            if (_count == 0) return;
            _count = 0;
            _dirty = true;
            InvalidateVisual();
        }

        public void SetProfile(IReadOnlyList<PixelSample> samples)
        {
            bool gray = samples.Count > 0 && samples[0].IsGrayscale;
            bool changed = _count != samples.Count || _gray != gray;
            _count = samples.Count;
            _gray = gray;
            for (int channel = 0; channel < 3; channel++)
            {
                if (_values[channel].Length < _count)
                    _values[channel] = new double[Math.Max(_count, Math.Max(16, _values[channel].Length * 2))];
                var destination = _values[channel];
                for (int i = 0; i < _count; i++)
                {
                    var sample = samples[i];
                    double raw = channel == 0 ? (_gray ? sample.Gray : sample.R) : channel == 1 ? sample.G : sample.B;
                    double value = double.IsFinite(raw) ? raw : double.NaN;
                    changed |= !destination[i].Equals(value);
                    destination[i] = value;
                }
            }
            if (!changed) return;
            _dirty = true;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            if (w <= 2 || h <= 2 || _count == 0) return;
            const double left = 36, top = 18, right = 20, bottom = 32;
            double pw = Math.Max(1, w - left - right), ph = Math.Max(1, h - top - bottom);
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double min = 0, max = 1;
            for (int channel = 0; channel < ChannelCount; channel++)
                for (int i = 0; i < _count; i++)
                    if (double.IsFinite(_values[channel][i]))
                    {
                        min = Math.Min(min, _values[channel][i]);
                        max = Math.Max(max, _values[channel][i]);
                    }
            double step = TickStep(max - min, Math.Clamp((int)(ph / 45), 2, 12));
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
            DrawAxes(dc, min, max, Math.Max(1, _count - 1), left, top, pw, ph, dpi);
            if (_dirty || _geometrySize != RenderSize || _geometryDpi != dpi)
            {
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        _points.Clear();
                        for (int i = 0; i < _count; i++)
                        {
                            double value = _values[channel][i];
                            if (!double.IsFinite(value)) { Flush(context); continue; }
                            _points.Add(new Point(left + (_count == 1 ? 0 : pw * i / (_count - 1)), top + ph * (1 - (value - min) / (max - min))));
                        }
                        Flush(context);
                    }
                    geometry.Freeze();
                    _curves[channel] = geometry;
                }
                _geometrySize = RenderSize;
                _geometryDpi = dpi;
                _dirty = false;
            }
            for (int channel = 0; channel < ChannelCount; channel++)
                dc.DrawGeometry(null, CurvePens[channel], _curves[channel]);
        }

        // Preserve endpoints and both extrema in source order within each physical
        // pixel column. Each finite run is reduced independently, preserving gaps.
        internal static void ReduceToPixelColumns(List<Point> points, double dpiScale)
        {
            int write = 0;
            for (int start = 0; start < points.Count;)
            {
                int end = start + 1, min = start, max = start;
                double column = Math.Floor(points[start].X * dpiScale);
                while (end < points.Count && Math.Floor(points[end].X * dpiScale) == column)
                {
                    if (points[end].Y < points[min].Y) min = end;
                    if (points[end].Y > points[max].Y) max = end;
                    end++;
                }
                int first = Math.Min(min, max), second = Math.Max(min, max);
                // Read before compacting: the output can overlap the input bucket.
                Point a = points[start], b = points[first], c = points[second], d = points[end - 1];
                points[write++] = a;
                if (first != start) points[write++] = b;
                if (second != first && second != start) points[write++] = c;
                if (end - 1 != second && end - 1 != start) points[write++] = d;
                start = end;
            }
            if (write < points.Count) points.RemoveRange(write, points.Count - write);
        }

        private void Flush(StreamGeometryContext context)
        {
            if (_points.Count == 0) return;
            ReduceToPixelColumns(_points, VisualTreeHelper.GetDpi(this).DpiScaleX);
            context.BeginFigure(_points[0], false, false);
            for (int i = 1; i < _points.Count; i++)
                context.LineTo(_points[i], true, false);
            _points.Clear();
        }
    }
}
