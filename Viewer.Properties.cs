using Fizzy.ImageViewer.Constants;
using System;
using System.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>
    /// 指示当前是否可以接受新的渲染请求。
    /// 当渲染队列未满时返回 true。
    /// </summary>
    public bool CanRefresh => Interlocked.CompareExchange(ref _renderingCount, 0, 0) < Interlocked.CompareExchange(ref _maxRenderQueue, 0, 0);

    /// <summary>
    /// 渲染队列最大深度。
    /// <para>
    /// 1 = 纯跳帧（最低延迟），3 = 默认（平衡），更大 = 更流畅但延迟更高。
    /// </para>
    /// </summary>
    public int MaxRenderQueue
    {
        get => Interlocked.CompareExchange(ref _maxRenderQueue, 0, 0);
        set => Interlocked.Exchange(ref _maxRenderQueue, Math.Max(RenderingConstants.MinRenderQueue, value));
    }

    /// <summary>
    /// 是否允许用户通过点击关闭按钮关闭窗口。
    /// </summary>
    public bool CanUserClose
    {
        get => _window.CanUserClose;
        set => _window.CanUserClose = value;
    }

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public double Width
    {
        get => _window.Dispatcher.Invoke(() => _window.Width);
        set => _window.Dispatcher.Invoke(() => _window.Width = value);
    }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public double Height
    {
        get => _window.Dispatcher.Invoke(() => _window.Height);
        set => _window.Dispatcher.Invoke(() => _window.Height = value);
    }

    /// <summary>
    /// 窗口左边缘位置。
    /// </summary>
    public double Left
    {
        get => _window.Dispatcher.Invoke(() => _window.Left);
        set => _window.Dispatcher.Invoke(() => _window.Left = value);
    }

    /// <summary>
    /// 窗口顶边缘位置。
    /// </summary>
    public double Top
    {
        get => _window.Dispatcher.Invoke(() => _window.Top);
        set => _window.Dispatcher.Invoke(() => _window.Top = value);
    }

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title
    {
        get => _window.Dispatcher.Invoke(() => _window.Title);
        set => _window.Dispatcher.Invoke(() => _window.Title = value);
    }

    /// <summary>
    /// 右上角 HUD 标签文本。
    /// </summary>
    public string? Label
    {
        get => _window.Dispatcher.Invoke(() => _window.Layer2.Label);
        set => _window.Dispatcher.Invoke(() => _window.Layer2.Label = value);
    }

    /// <summary>
    /// 是否启用无边框模式。
    /// </summary>
    public bool Borderless
    {
        get => _window.Dispatcher.Invoke(() => _window.Borderless);
        set => _window.Dispatcher.Invoke(() => _window.Borderless = value);
    }
}
