using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Management and convenience drawing in the default marker layer.</summary>
public interface IViewerDrawing
{
    /// <summary>The entire default drawing layer, including its content and input policy.</summary>
    DrawingLayer Markers { get; }

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层创建初始内容为线段的绘图；返回句柄可整体替换为单个元素或集合。
    /// </summary>
    DrawingHandle DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层创建初始内容为文本标签的绘图；返回句柄可整体替换为单个元素或集合。
    /// </summary>
    DrawingHandle DrawText(Point anchor, string text, Brush brush, double fontSize = 14, Vector offset = default);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层创建初始内容为准星的绘图；返回句柄可整体替换为单个元素或集合。
    /// </summary>
    DrawingHandle DrawCrosshair(Point center, Brush brush, double armLength = 20, double thickness = 2);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层创建初始内容为矩形框的绘图；返回句柄可整体替换为单个元素或集合。
    /// </summary>
    DrawingHandle DrawRectangle(Rect rect, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层创建初始内容为圆形的绘图；返回句柄可整体替换为单个元素或集合。
    /// </summary>
    DrawingHandle DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null);
}
