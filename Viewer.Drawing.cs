using Fizzy.ImageViewer.Internal;
using Fizzy.ImageViewer.Enums;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>
    /// 在覆盖层上绘制一条线段。
    /// </summary>
    /// <param name="p1">起点（图像坐标）</param>
    /// <param name="p2">终点（图像坐标）</param>
    /// <param name="brush">画刷颜色</param>
    /// <param name="thickness">线宽</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该线段</returns>
    public IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0)
    {
        return InvokeAndCreateHandle(() =>
        {
            var line = Shapes.CreateLine();
            line.X1 = p1.X; line.Y1 = p1.Y;
            line.X2 = p2.X; line.Y2 = p2.Y;
            line.Stroke = brush;
            line.StrokeThickness = thickness;
            return line;
        });
    }

    /// <summary>
    /// 在覆盖层上绘制文本标签。
    /// </summary>
    /// <param name="anchor">锚点位置（图像坐标）</param>
    /// <param name="text">文本内容</param>
    /// <param name="brush">文本颜色</param>
    /// <param name="fontSize">字体大小</param>
    /// <param name="offset">相对于锚点的屏幕偏移量</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该标签</returns>
    public IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default)
    {
        return InvokeAndCreateHandle(() =>
        {
            var tb = Shapes.CreateLabel(anchor, text, offset.X, offset.Y);
            tb.Foreground = brush;
            tb.FontSize = fontSize;
            return tb;
        });
    }

    /// <summary>
    /// 在覆盖层上绘制准星。
    /// </summary>
    /// <param name="center">中心点（图像坐标）</param>
    /// <param name="brush">画刷颜色</param>
    /// <param name="size">准星大小</param>
    /// <param name="thickness">线宽</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该准星</returns>
    public IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2)
    {
        return InvokeAndCreateHandle(() =>
        {
            var path = Shapes.CreateCrosshair(center, size, thickness);
            path.Stroke = brush;
            return path;
        });
    }

    /// <summary>
    /// 在覆盖层上绘制矩形框。
    /// </summary>
    /// <param name="rect">矩形区域（图像坐标）</param>
    /// <param name="brush">边框颜色</param>
    /// <param name="thickness">边框宽度</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该矩形</returns>
    public IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0)
    {
        return InvokeAndCreateHandle(() =>
        {
            var shape = Shapes.CreateRectangle();
            shape.Width = rect.Width;
            shape.Height = rect.Height;
            shape.Stroke = brush;
            shape.StrokeThickness = thickness;
            Canvas.SetLeft(shape, rect.Left);
            Canvas.SetTop(shape, rect.Top);
            return shape;
        });
    }

    /// <summary>
    /// 在覆盖层上绘制圆形。
    /// </summary>
    /// <param name="center">圆心（图像坐标）</param>
    /// <param name="radius">半径（图像坐标）</param>
    /// <param name="brush">边框颜色</param>
    /// <param name="thickness">边框宽度</param>
    /// <param name="fill">填充画刷，null 表示不填充</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该圆形</returns>
    public IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null)
    {
        return InvokeAndCreateHandle(() =>
        {
            var path = Shapes.CreateCircle(center, radius);
            path.Stroke = brush;
            path.StrokeThickness = thickness;
            if (fill != null) path.Fill = fill;
            return path;
        });
    }

    /// <summary>
    /// 清除覆盖层上的所有形状。
    /// </summary>
    public void ClearShapes()
        => _window?.Dispatcher.Invoke(() => _window.Layer1.Clear());

    /// <summary>
    /// 在 HUD 层绘制或更新文本显示（屏幕坐标，不随图像缩放）。
    /// </summary>
    /// <param name="text">要显示的文本</param>
    /// <param name="brush">文本颜色</param>
    /// <param name="existing">已有的绘图句柄。为 null 时创建新元素，不为 null 时更新已有元素的文本和颜色。</param>
    /// <param name="anchor">屏幕坐标锚点。为 null 时拼接到左上角 StackPanel；不为 null 时放置在指定屏幕位置。</param>
    /// <param name="alignment">锚点在文本上的对齐位置（九宫格）。仅在 anchor 不为 null 时生效。</param>
    /// <param name="fontSize">字体大小</param>
    /// <returns>IDisposable 句柄，Dispose 时移除该 HUD 文本</returns>
    public IDisposable DrawHudText(string text, Brush brush, IDisposable? existing = null,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft,
        double fontSize = 14)
    {
        if (_window == null)
            return new DrawingHandle(() => { });

        if (existing is DrawingHandle handle && handle.State is TextBlock existingTb)
        {
            // 更新已有元素
            try
            {
                _window.Dispatcher.Invoke(() => _window.Layer2.UpdateText(existingTb, text, brush));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Viewer.DrawHudText] Failed to update HUD text");
            }
            return existing;
        }

        // 创建新元素
        try
        {
            return _window.Dispatcher.Invoke(() =>
            {
                TextBlock tb;
                if (anchor.HasValue)
                {
                    tb = _window.Layer2.AddTextAt(text, brush, fontSize, anchor.Value, alignment);
                }
                else
                {
                    tb = _window.Layer2.AddText(text, brush, fontSize);
                }

                var newHandle = new DrawingHandle(() =>
                {
                    _window.Dispatcher.InvokeAsync(() => _window.Layer2.RemoveText(tb));
                });
                newHandle.State = tb;
                return newHandle;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Viewer.DrawHudText] Failed to create HUD text");
            return new DrawingHandle(() => { });
        }
    }

    /// <summary>
    /// 在 UI 线程上创建形状并返回管理句柄。
    /// </summary>
    /// <param name="createShapeFactory">创建形状的工厂方法</param>
    /// <returns>IDisposable 句柄，Dispose 时从覆盖层移除该形状</returns>
    private DrawingHandle InvokeAndCreateHandle(Func<UIElement> createShapeFactory) => _window.Dispatcher.Invoke(() =>
    {
        var shape = createShapeFactory();
        _window.Layer1.AddShape(shape);

        return new DrawingHandle(() =>
        {
            _window.Dispatcher.InvokeAsync(() => _window.Layer1.RemoveShape(shape));
        });
    });
}
