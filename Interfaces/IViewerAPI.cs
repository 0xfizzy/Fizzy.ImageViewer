using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer.Interfaces;

/// <summary>
/// 图像查看器的完整 API 接口。
/// </summary>
public interface IViewerAPI : IDisposable, IAsyncDisposable
{
    // === 窗口管理 ===

    /// <summary>
    /// 显示窗口。
    /// </summary>
    void Show();

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    void Close();

    /// <summary>
    /// 是否允许用户通过点击关闭按钮关闭窗口。
    /// </summary>
    bool CanUserClose { get; set; }

    /// <summary>
    /// 窗口标题。
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    double Width { get; set; }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    double Height { get; set; }

    /// <summary>
    /// 窗口左边缘位置。
    /// </summary>
    double Left { get; set; }

    /// <summary>
    /// 窗口顶边缘位置。
    /// </summary>
    double Top { get; set; }

    /// <summary>
    /// 是否启用无边框模式。
    /// </summary>
    bool Borderless { get; set; }

    ValueTask<Fizzy.ImageViewer.Frames.FrameSubmitResult> SubmitFrameAsync(
        Fizzy.ImageViewer.Frames.ImageFrame frame,
        Fizzy.ImageViewer.Frames.FrameSubmissionOptions? options = null,
        CancellationToken ct = default);
    Fizzy.ImageViewer.Frames.FrameLease? AcquireCurrentFrame();
    event Action<Fizzy.ImageViewer.Frames.FrameInfo>? FrameCommitted;
    Fizzy.ImageViewer.Imaging.GrayDisplayRange? DisplayRange { get; set; }
    ValueTask CloseAsync();
    Task<Fizzy.ImageViewer.Snapshots.ImageSnapshot> CaptureSnapshotAsync(
        Fizzy.ImageViewer.Snapshots.SnapshotKind kind, CancellationToken ct = default);
    // === 绘图 ===

    /// <summary>
    /// 在覆盖层上绘制一条线段。
    /// </summary>
    IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在覆盖层上绘制文本标签。
    /// </summary>
    IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default);

    /// <summary>
    /// 在覆盖层上绘制准星。
    /// </summary>
    IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2);

    /// <summary>
    /// 在覆盖层上绘制矩形框。
    /// </summary>
    IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在覆盖层上绘制圆形。
    /// </summary>
    IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null);

    /// <summary>
    /// 清除覆盖层上的所有形状。
    /// </summary>
    void ClearShapes();

    /// <summary>
    /// 在 HUD 层绘制或更新文本显示。
    /// </summary>
    IDisposable DrawHudText(string text, Brush brush, IDisposable? existing = null,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft,
        double fontSize = 14);

    // === 测量工具 ===

    /// <summary>
    /// 程序化启动已注册的测量工具。
    /// </summary>
    void StartMeasure(string methodName);

    /// <summary>
    /// 取消当前进行中的测量。
    /// </summary>
    void CancelMeasure();

    // === 扩展 ===

    /// <summary>
    /// 注册自定义菜单项。
    /// </summary>
    void RegisterMenu(IMenuItem menuItem);

    /// <summary>
    /// 注册自定义测量方法。
    /// </summary>
    void RegisterMeasureMethod(IMeasureMethod method);
}
