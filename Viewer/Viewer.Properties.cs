using System.Windows;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public bool CanUserClose
    {
        get => InvokeAlive(() => _window.CanUserClose);
        set => InvokeAlive(() => _window.CanUserClose = value);
    }

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public double Width
    {
        get => InvokeAlive(() => _window.Width);
        set => InvokeAlive(() => _window.Width = value);
    }

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public double Height
    {
        get => InvokeAlive(() => _window.Height);
        set => InvokeAlive(() => _window.Height = value);
    }

    /// <summary>
    /// 窗口左边缘位置。
    /// </summary>
    public double Left
    {
        get => InvokeAlive(() => _window.Left);
        set => InvokeAlive(() => _window.Left = value);
    }

    /// <summary>
    /// 窗口顶边缘位置。
    /// </summary>
    public double Top
    {
        get => InvokeAlive(() => _window.Top);
        set => InvokeAlive(() => _window.Top = value);
    }

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title
    {
        get => InvokeAlive(() => _window.Title);
        set => InvokeAlive(() => _window.Title = value);
    }

    /// <summary>
    /// 右上角 HUD 标签文本。
    /// </summary>
    public string? Label
    {
        get => InvokeAlive(() => _window.HudLayer.Label);
        set => InvokeAlive(() => _window.HudLayer.Label = value);
    }

    /// <summary>
    /// 是否启用无边框模式。
    /// </summary>
    public bool Borderless
    {
        get => InvokeAlive(() => _window.Borderless);
        set => InvokeAlive(() => _window.Borderless = value);
    }

    /// <inheritdoc />
    public void Show() => InvokeAlive(() =>
    {
        if (_window!.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();   // raise above any maximized/foreground window
    });

    /// <inheritdoc />
    public void Hide() => InvokeAlive(() => _window.Hide());

    /// <inheritdoc />
    public void Minimize() => InvokeAlive(() => _window.WindowState = WindowState.Minimized);

    /// <inheritdoc />
    public bool IsVisible => InvokeAlive(() => _window.IsVisible);

    /// <inheritdoc />
    public bool IsMinimized => InvokeAlive(() => _window.WindowState == WindowState.Minimized);

    public void FitImageToContainer()
    {
        InvokeAlive(() => _window.ImageLayer.FitImageToContainer());
    }
}
