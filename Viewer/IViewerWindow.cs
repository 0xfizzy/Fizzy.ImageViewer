namespace Fizzy.ImageViewer;

/// <summary>Borrowed window control. Operations dispatch to the viewer STA and reject calls once shutdown starts.</summary>
public interface IViewerWindow
{
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

    /// <summary>Window closure notification on the viewer STA. Owners must still await IViewer.DisposeAsync
    /// to drain background work and wait for actual STA termination.</summary>
    event EventHandler? Closed;

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
}
