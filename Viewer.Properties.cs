using Fizzy.ImageViewer.Constants;
using System;
using System.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public bool CanUserClose
    {
        get => _window.Dispatcher.Invoke(() => _window.CanUserClose);
        set => _window.Dispatcher.Invoke(() => _window.CanUserClose = value);
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

public partial class Viewer
{
    public Fizzy.ImageViewer.Imaging.PixelQueryOptions QueryOptions
    {
        get => _measureManager!.Context.QueryOptions;
        set => _window.Dispatcher.Invoke(() => _measureManager!.Context.QueryOptions = value);
    }
    public Fizzy.ImageViewer.Imaging.PixelQueryMetrics QueryMetrics => _measureManager!.Context.QueryMetrics;
}
