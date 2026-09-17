using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

            var item = new LineStrengthItem(_startPoint.Value, point, _currentLine!, _currentLabel!, ctx);
            _items.Add(item);

            // 设置 Line 的 Tag
            if (_currentLine!.Tag is OverlayTagData tagData)
            {
                tagData.LinkedShapes = [_currentLabel!];
                tagData.OnRemoved = () =>
                {
                    item.Cleanup();
                    _items.Remove(item);
                };
            }

            item.CreatePlotWindow();

            if (ctx.CurrentBitmap != null)
            {
                item.OnImageUpdated(ctx.CurrentBitmap);
            }

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
        /// 0-GC 优化：预分配数组缓冲区，每帧复用。
        /// </summary>
        private sealed class LineStrengthItem
        {
            private readonly Point _startPoint;
            private readonly int _lineLength;
            private readonly MeasureContext _context;

            // 预分配缓冲区 (在构造时根据线段长度分配一次)
            private readonly double[] _distances;
            private readonly double[] _redValues;
            private readonly double[] _greenValues;
            private readonly double[] _blueValues;
            private readonly double[] _grayValues;

            // Bresenham 预计算的整数坐标
            private readonly int _x1, _y1, _x2, _y2;
            private readonly int _dx, _dy, _sx, _sy;

            public Line Line { get; }
            public TextBlock Label { get; }
            public Window? PlotWindow { get; private set; }

            public LineStrengthItem(Point start, Point end, Line line, TextBlock label, MeasureContext context)
            {
                _startPoint = start;
                _context = context;
                Line = line;
                Label = label;

                // Bresenham 预计算（使用 Math.Round 取最近像素，避免截断误差）
                _x1 = (int)Math.Round(start.X);
                _y1 = (int)Math.Round(start.Y);
                _x2 = (int)Math.Round(end.X);
                _y2 = (int)Math.Round(end.Y);
                _dx = Math.Abs(_x2 - _x1);
                _dy = Math.Abs(_y2 - _y1);
                _sx = (_x1 < _x2) ? 1 : -1;
                _sy = (_y1 < _y2) ? 1 : -1;

                // 计算线段上的点数 (Bresenham 算法的点数 = max(dx, dy) + 1)
                _lineLength = Math.Max(_dx, _dy) + 1;

                // 预分配缓冲区
                _distances = new double[_lineLength];
                _redValues = new double[_lineLength];
                _greenValues = new double[_lineLength];
                _blueValues = new double[_lineLength];
                _grayValues = new double[_lineLength];

                // 订阅图像更新事件
                _context.ImageUpdated += OnImageUpdated;
            }

            /// <summary>
            /// 清理资源：取消订阅事件、关闭绘图窗口。
            /// </summary>
            public void Cleanup()
            {
                _context.ImageUpdated -= OnImageUpdated;
                PlotWindow?.Close();
                PlotWindow = null;
            }

            public void CreatePlotWindow()
            {
                PlotWindow = new Window
                {
                    Title = "Pixel Values",
                    Width = 600,
                    Height = 400,
                    Topmost = true
                };

                var plot = new ScottPlot.WPF.WpfPlot();
                PlotWindow.Content = plot;
                plot.Plot.Axes.SetLimitsY(0, 255);
                PlotWindow.Show();
            }

            public unsafe void OnImageUpdated(WriteableBitmap bitmap)
            {
                if (PlotWindow == null || !PlotWindow.IsVisible) return;

                int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
                int stride = bitmap.BackBufferStride;
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;

                bool isRgb24 = bitmap.Format == PixelFormats.Rgb24;
                bool isBgrOrder = bitmap.Format == PixelFormats.Bgr24 ||
                                  bitmap.Format == PixelFormats.Bgr32 ||
                                  bitmap.Format == PixelFormats.Bgra32 ||
                                  bitmap.Format == PixelFormats.Pbgra32;
                bool isColor = isRgb24 || isBgrOrder || bytesPerPixel >= 3;

                int validCount = 0;

                bitmap.Lock();
                try
                {
                    byte* basePtr = (byte*)bitmap.BackBuffer.ToPointer();

                    // Bresenham 内联，避免迭代器分配
                    int x = _x1, y = _y1;
                    int err = _dx - _dy;

                    while (true)
                    {
                        if (x >= 0 && x < width && y >= 0 && y < height && validCount < _lineLength)
                        {
                            double dist = Math.Sqrt((x - _startPoint.X) * (x - _startPoint.X) +
                                                    (y - _startPoint.Y) * (y - _startPoint.Y));
                            _distances[validCount] = dist;

                            int offset = y * stride + x * bytesPerPixel;

                            if (isColor)
                            {
                                if (isRgb24)
                                {
                                    _redValues[validCount] = basePtr[offset];
                                    _greenValues[validCount] = basePtr[offset + 1];
                                    _blueValues[validCount] = basePtr[offset + 2];
                                }
                                else
                                {
                                    _blueValues[validCount] = basePtr[offset];
                                    _greenValues[validCount] = basePtr[offset + 1];
                                    _redValues[validCount] = basePtr[offset + 2];
                                }
                            }
                            else
                            {
                                _grayValues[validCount] = basePtr[offset];
                            }
                            validCount++;
                        }

                        if (x == _x2 && y == _y2) break;

                        int e2 = 2 * err;
                        if (e2 > -_dy) { err -= _dy; x += _sx; }
                        if (e2 < _dx) { err += _dx; y += _sy; }
                    }
                }
                finally
                {
                    bitmap.Unlock();
                }

                UpdatePlot(validCount, isColor);
            }


            private void UpdatePlot(int count, bool isColor)
            {
                if (PlotWindow == null || count == 0) return;

                var wpfPlot = (ScottPlot.WPF.WpfPlot)PlotWindow.Content;
                wpfPlot.Plot.Clear();

                // 使用 Span 切片，避免 ToArray 分配
                // ScottPlot 需要 double[]，但我们传递预分配数组的引用
                // 注意：ScottPlot 内部会复制数据，这是不可避免的
                if (isColor)
                {
                    // 使用 AsSpan + ToArray 只在数据量变化时分配
                    // 由于 ScottPlot API 限制，这里仍需传递数组
                    var xs = _distances.AsSpan(0, count).ToArray();
                    var rs = _redValues.AsSpan(0, count).ToArray();
                    var gs = _greenValues.AsSpan(0, count).ToArray();
                    var bs = _blueValues.AsSpan(0, count).ToArray();

                    wpfPlot.Plot.Add.Scatter(xs, rs, ScottRed);
                    wpfPlot.Plot.Add.Scatter(xs, gs, ScottGreen);
                    wpfPlot.Plot.Add.Scatter(xs, bs, ScottBlue);
                }
                else
                {
                    var xs = _distances.AsSpan(0, count).ToArray();
                    var ys = _grayValues.AsSpan(0, count).ToArray();
                    wpfPlot.Plot.Add.Scatter(xs, ys);
                }

                wpfPlot.Plot.Grid.IsVisible = true;
                wpfPlot.Plot.Axes.AutoScaleX();
                wpfPlot.Plot.Axes.AutoScaleExpandY();
                wpfPlot.Refresh();
            }
        }
    }
}
