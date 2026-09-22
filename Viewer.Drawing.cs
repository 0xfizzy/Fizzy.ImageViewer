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
    /// <summary>Draws a non-interactive single-element batch in the Markers layer.</summary>
    public IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.LineElement(p1, p2, brush, thickness)]);

    public IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default) =>
        Layers.Markers.AddBatch([new Drawing.TextElement(anchor, text, brush, fontSize, offset)]);

    public IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2) =>
        Layers.Markers.AddBatch([new Drawing.CrosshairElement(center, brush, size, thickness)]);

    public IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.RectangleElement(rect, brush, thickness)]);

    public IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        Layers.Markers.AddBatch([new Drawing.CircleElement(center, radius, brush, thickness, fill)]);

    /// <summary>Clears all business layers, including measurements, without clearing the HUD.</summary>
    public void ClearShapes() => Layers.Clear();
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

}
