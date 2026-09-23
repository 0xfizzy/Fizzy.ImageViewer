using System.Windows;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public bool CanUserClose
    {
        get => InvokeAlive(() => _runtime.Window.CanUserClose);
        set => InvokeAlive(() => _runtime.Window.CanUserClose = value);
    }

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public double Width
    {
        get => InvokeAlive(() => _runtime.Window.Width);
        set => InvokeAlive(() => _runtime.Window.Width = value);
    }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public double Height
    {
        get => InvokeAlive(() => _runtime.Window.Height);
        set => InvokeAlive(() => _runtime.Window.Height = value);
    }

    /// <summary>
    /// 窗口左边缘位置。
    /// </summary>
    public double Left
    {
        get => InvokeAlive(() => _runtime.Window.Left);
        set => InvokeAlive(() => _runtime.Window.Left = value);
    }

    /// <summary>
    /// 窗口顶边缘位置。
    /// </summary>
    public double Top
    {
        get => InvokeAlive(() => _runtime.Window.Top);
        set => InvokeAlive(() => _runtime.Window.Top = value);
    }

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title
    {
        get => InvokeAlive(() => _runtime.Window.Title);
        set => InvokeAlive(() => _runtime.Window.Title = value);
    }

    /// <summary>
    /// 右上角 HUD 标签文本。
    /// </summary>
    public string? Label
    {
        get => InvokeAlive(() => _runtime.Window.HudLayer.Label);
        set => InvokeAlive(() => _runtime.Window.HudLayer.Label = value);
    }

    /// <summary>
    /// 是否启用无边框模式。
    /// </summary>
    public bool Borderless
    {
        get => InvokeAlive(() => _runtime.Window.Borderless);
        set => InvokeAlive(() => _runtime.Window.Borderless = value);
    }

    /// <inheritdoc />
    public void Show() => InvokeAlive(() =>
    {
        if (_runtime.Window.WindowState == WindowState.Minimized)
            _runtime.Window.WindowState = WindowState.Normal;
        _runtime.Window.Show();
        _runtime.Window.Activate();   // raise above any maximized/foreground window
    });

    /// <inheritdoc />
    public void Hide() => InvokeAlive(() => _runtime.Window.Hide());

    /// <inheritdoc />
    public void Minimize() => InvokeAlive(() => _runtime.Window.WindowState = WindowState.Minimized);

    /// <inheritdoc />
    public bool IsVisible => InvokeAlive(() => _runtime.Window.IsVisible);

    /// <inheritdoc />
    public bool IsMinimized => InvokeAlive(() => _runtime.Window.WindowState == WindowState.Minimized);

    public void FitImageToContainer()
    {
        InvokeAlive(() => _runtime.Window.ImageLayer.FitImageToContainer());
    }
}
