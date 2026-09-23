using System.Windows;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public bool CanUserClose
    {
        get => InvokeAlive(() => _host.Window.CanUserClose);
        set => InvokeAlive(() => _host.Window.CanUserClose = value);
    }

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public double Width
    {
        get => InvokeAlive(() => _host.Window.Width);
        set => InvokeAlive(() => _host.Window.Width = value);
    }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public double Height
    {
        get => InvokeAlive(() => _host.Window.Height);
        set => InvokeAlive(() => _host.Window.Height = value);
    }

    /// <summary>
    /// 窗口左边缘位置。
    /// </summary>
    public double Left
    {
        get => InvokeAlive(() => _host.Window.Left);
        set => InvokeAlive(() => _host.Window.Left = value);
    }

    /// <summary>
    /// 窗口顶边缘位置。
    /// </summary>
    public double Top
    {
        get => InvokeAlive(() => _host.Window.Top);
        set => InvokeAlive(() => _host.Window.Top = value);
    }

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title
    {
        get => InvokeAlive(() => _host.Window.Title);
        set => InvokeAlive(() => _host.Window.Title = value);
    }

    /// <summary>
    /// 右上角 HUD 标签文本。
    /// </summary>
    public string? Label
    {
        get => InvokeAlive(() => _host.Window.HudLayer.Label);
        set => InvokeAlive(() => _host.Window.HudLayer.Label = value);
    }

    /// <summary>
    /// 是否启用无边框模式。
    /// </summary>
    public bool Borderless
    {
        get => InvokeAlive(() => _host.Window.Borderless);
        set => InvokeAlive(() => _host.Window.Borderless = value);
    }

    /// <inheritdoc />
    public void Show() => InvokeAlive(() =>
    {
        if (_host.Window.WindowState == WindowState.Minimized)
            _host.Window.WindowState = WindowState.Normal;
        _host.Window.Show();
        _host.Window.Activate();   // raise above any maximized/foreground window
    });

    /// <inheritdoc />
    public void Hide() => InvokeAlive(() => _host.Window.Hide());

    /// <inheritdoc />
    public void Minimize() => InvokeAlive(() => _host.Window.WindowState = WindowState.Minimized);

    /// <inheritdoc />
    public bool IsVisible => InvokeAlive(() => _host.Window.IsVisible);

    /// <inheritdoc />
    public bool IsMinimized => InvokeAlive(() => _host.Window.WindowState == WindowState.Minimized);

    public void FitImageToContainer()
    {
        InvokeAlive(() => _host.Window.ImageLayer.FitImageToContainer());
    }
}
