using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.MeasureMethods
{
    /// <summary>
    /// 线段强度测量：绘制线段并实时显示沿线像素值曲线图。
    /// </summary>
    public class LineStrengthMeasure : Interfaces.IMeasureMethod
    {
        public string Name => "LineStrength";

        private Point? _startPoint;
        private Line? _currentLine;
        private TextBlock? _currentLabel;
        private MeasureContext? _currentContext;

        // 已完成的测量项及其对应的绘图窗口
        private readonly System.Collections.Generic.List<LineStrengthItem> _items = [];

        private static readonly ScottPlot.Color ScottRed = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
        private static readonly ScottPlot.Color ScottGreen = ScottPlot.Color.FromColor(System.Drawing.Color.Green);
        private static readonly ScottPlot.Color ScottBlue = ScottPlot.Color.FromColor(System.Drawing.Color.Blue);

        public bool OnClick(Point point, MeasureContext ctx)
        {
            if (_startPoint == null)
            {
                _startPoint = point;
                _currentContext = ctx;

                _currentLine = Shapes.CreateLine();
                _currentLine.X1 = point.X;
                _currentLine.Y1 = point.Y;
                _currentLine.X2 = point.X;
                _currentLine.Y2 = point.Y;
                ctx.AddShape(_currentLine);

                _currentLabel = Shapes.CreateLabel(point, "0.0 px", 5, 0);
                ctx.AddShape(_currentLabel);

                return false;
            }

            UpdateShape(point, ctx);

            var item = new LineStrengthItem(_currentLine!, ctx, item => _items.Remove(item));
            _items.Add(item);

            // 设置 Line 的 Tag
            if (_currentLine!.Tag is OverlayTagData tagData)
            {
                tagData.LinkedShapes = [_currentLabel!];
                tagData.OnRemoved = item.OnLineRemoved;
            }

            item.CreatePlotWindow();

            Reset();
            return true;
        }

        public void OnMouseMove(Point point, MeasureContext ctx)
        {
            if (_startPoint != null)
            {
                UpdateShape(point, ctx);
            }
        }

        public void Cancel(MeasureContext ctx)
        {
            if (_currentLine != null) ctx.RemoveShape(_currentLine);
            if (_currentLabel != null) ctx.RemoveShape(_currentLabel);
            Reset();
        }

        private void UpdateShape(Point endPoint, MeasureContext ctx)
        {
            if (_currentLine == null || _currentLabel == null || _startPoint == null) return;

            _currentLine.X2 = endPoint.X;
            _currentLine.Y2 = endPoint.Y;

            double dist = Math.Sqrt(Math.Pow(endPoint.X - _startPoint.Value.X, 2) +
                                    Math.Pow(endPoint.Y - _startPoint.Value.Y, 2));
            _currentLabel.Text = $"{dist:F1} px";
            ctx.UpdateAnchor(_currentLabel, endPoint);
        }

        private void Reset()
        {
            _startPoint = null;
            _currentLine = null;
            _currentLabel = null;
            _currentContext = null;
        }


        /// <summary>
        /// 单个线段强度测量项，包含线段坐标和绘图窗口。
        /// 后台采样并复用曲线缓冲，UI 只发布已完成的结果。
        /// </summary>
        private sealed class LineStrengthItem : IFrameMeasurement
        {
            private readonly Line _line;
            private QueryRequest? _cached;
            private object? _cachedGeometry;
            private readonly MeasureContext _context;
            private readonly Action<LineStrengthItem> _onDisposed;
            private Imaging.LineProfile _profile = new();
            private double[] _xs = [], _rs = [], _gs = [], _bs = [];
            private ScottPlot.Plottables.Scatter? _red, _green, _blue;
            private volatile bool _disposed;
            public Window? PlotWindow { get; private set; }
            public LineStrengthItem(Line line, MeasureContext context, Action<LineStrengthItem> onDisposed)
            { _line = line; _context = context; _onDisposed = onDisposed; }
            public void CreatePlotWindow()
            {
                PlotWindow = new Window { Title = "Pixel Values", Width = 600, Height = 400, Topmost = true, Content = new ScottPlot.WPF.WpfPlot() };
                PlotWindow.Closed += OnWindowClosed;
                PlotWindow.Show();
                _context.Register(this);
            }
            public QueryRequest? Capture(Frames.FrameDescriptor descriptor)
            {
                if(_disposed) return null;
                var start=new Point(_line.X1,_line.Y1); var end=new Point(_line.X2,_line.Y2);
                object geometry=(start,end,descriptor,(_line.Tag as OverlayTagData)?.GeometryVersion ?? 0);
                if(Equals(geometry,_cachedGeometry))return _cached;
                _cachedGeometry=geometry;
                var profile=new Imaging.LineProfile();
                var points=profile.Prepare(descriptor,start.X,start.Y,end.X,end.Y);
                return _cached=new(geometry,QueryKind.Line,points,null,(samples,_)=> { profile.Apply(samples!); _profile=profile; Publish(); });
            }
            public void ClearResult()
            {
                if(PlotWindow?.Content is ScottPlot.WPF.WpfPlot plot) { if(_red!=null) _red.IsVisible=false; if(_green!=null) _green.IsVisible=false; if(_blue!=null) _blue.IsVisible=false; plot.Refresh(); }
            }            private void Publish()
            {
                if (_disposed || PlotWindow == null) return;
                var plot = (ScottPlot.WPF.WpfPlot)PlotWindow.Content;
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
                PlotWindow.Title = "Pixel Values";
                plot.Refresh();
            }
            private void OnWindowClosed(object? sender, EventArgs e) => Cleanup(removeLine: true, closeWindow: false);
            public void OnLineRemoved() => Cleanup(removeLine: false, closeWindow: true);
            public void Dispose() => Cleanup(removeLine: true, closeWindow: true);
            private void Cleanup(bool removeLine, bool closeWindow)
            {
                if (_disposed) return;
                _disposed = true; _context.Unregister(this);
                var window = PlotWindow; PlotWindow = null;
                if (window != null) window.Closed -= OnWindowClosed;
                if (_line.Tag is OverlayTagData tag) tag.OnRemoved = null;
                _onDisposed(this);
                // OverlayLayer invokes OnRemoved before removing the line itself.
                // Only the window/context entry points should initiate shape removal.
                if (removeLine) _context.RemoveShape(_line);
                if (closeWindow) window?.Close();
            }
        }
    }
}
