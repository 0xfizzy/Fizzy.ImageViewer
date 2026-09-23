using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

/// <summary>
/// 图像查看器的完整 API 接口。
/// </summary>
public interface IViewerAPI : IAsyncDisposable
{
    /// <summary>Style for future measurements. Brushes are copied and frozen before UI dispatch.</summary>
    ShapeStyle MeasurementStyle { get; set; }
    // === 窗口管理 ===

    /// <summary>
    /// 在查看器 STA 上显示、恢复最小化并激活窗口。关闭或释放开始后抛出 ObjectDisposedException。
    /// </summary>
    void Show();
    /// <summary>在查看器 STA 上隐藏窗口，保留帧和绘图。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    void Hide();
    /// <summary>在查看器 STA 上最小化窗口。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    void Minimize();
    /// <summary>在查看器 STA 上查询可见性；已显示的最小化窗口仍可见。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    bool IsVisible { get; }
    /// <summary>在查看器 STA 上查询最小化状态。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    bool IsMinimized { get; }

    /// <summary>
    /// 是否允许用户通过点击关闭按钮关闭窗口。
    /// </summary>
    bool CanUserClose { get; set; }
    event EventHandler? Closed;
    void FitImageToContainer();

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
    /// <summary>右上角 HUD 标签，通过查看器 STA 读写。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    string? Label { get; set; }
    /// <summary>内置测量及像素 HUD 的查询配置，通过查看器 STA 读写。</summary>
    Fizzy.ImageViewer.Imaging.PixelQueryOptions QueryOptions { get; set; }
    /// <summary>内置查询调度器的统计快照，通过查看器 STA 读取。</summary>
    Fizzy.ImageViewer.Imaging.PixelQueryMetrics QueryMetrics { get; }

    ValueTask<Fizzy.ImageViewer.Frames.FrameSubmitResult> SubmitFrameAsync(
        Fizzy.ImageViewer.Frames.ImageFrame frame,
        Fizzy.ImageViewer.Frames.FrameSubmissionOptions? options = null,
        CancellationToken ct = default);
    Fizzy.ImageViewer.Frames.FrameLease? AcquireCurrentFrame();
    event Action<Fizzy.ImageViewer.Frames.FrameInfo>? FrameCommitted;
    Fizzy.ImageViewer.Imaging.GrayDisplayRange? DisplayRange { get; set; }
    Task<Fizzy.ImageViewer.Snapshots.ImageSnapshot> CaptureSnapshotAsync(
        Fizzy.ImageViewer.Snapshots.SnapshotKind kind, CancellationToken ct = default);
    Task<Fizzy.ImageViewer.Snapshots.ImageSnapshot> CaptureSnapshotAsync(
        Fizzy.ImageViewer.Snapshots.SnapshotKind kind, Fizzy.ImageViewer.Imaging.PixelRegion region, CancellationToken ct = default);
    // === 绘图 ===
    Fizzy.ImageViewer.Drawing.ViewerLayers Layers { get; }

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层绘制一条线段。
    /// </summary>
    IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层绘制文本标签。
    /// </summary>
    IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层绘制准星。
    /// </summary>
    IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层绘制矩形框。
    /// </summary>
    IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0);

    /// <summary>
    /// 在默认不参与命中测试的 Markers 图层绘制圆形。
    /// </summary>
    IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null);

    /// <summary>
    /// 取消测量并清空全部业务图层，保留 HUD。
    /// </summary>
    void ClearShapes();

    /// <summary>
    /// 在 HUD 层创建文本；通过返回句柄的 Update 更新文本和颜色。
    /// </summary>
    HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft,
        double fontSize = 14);

    // === 测量工具 ===

    /// <summary>
    /// 程序化启动已注册的测量工具。
    /// </summary>
    void StartMeasurement(string toolId);

    /// <summary>
    /// 取消当前进行中的测量。
    /// </summary>
    void CancelMeasurement();
    /// <summary>Completed built-in measurements only; raised on the viewer STA, excluding previews.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementCompleted;
    /// <summary>Removal of completed built-in measurements; raised on the viewer STA, including closure.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementRemoved;

    // === 扩展 ===

    /// <summary>
    /// 注册自定义菜单项。
    /// </summary>
    void RegisterMenu(IMenuItem menuItem);

    /// <summary>
    /// 注册自定义测量方法。工具回调中通过 IMeasurementToolContext.CreateScope 登记视觉元素和资源，
    /// 调用 Complete 保留完成结果；取消、删除、清空和关闭由查看器统一清理。
    /// 调度器、内部测量模型及逐帧查询注册不是公共扩展接口。
    /// </summary>
    void RegisterMeasurementTool(IMeasurementTool tool);
    bool UnregisterMeasurementTool(string toolId);
}
