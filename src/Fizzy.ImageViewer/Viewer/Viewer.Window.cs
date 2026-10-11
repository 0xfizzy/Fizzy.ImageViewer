using System.Windows;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <inheritdoc/>
    public bool CanUserClose
    {
        get => InvokeAlive(() => _host.Window.CanUserClose);
        set => InvokeAlive(() => _host.Window.CanUserClose = value);
    }

    /// <summary>
    /// Window width in device-independent units.
    /// </summary>
    public double Width
    {
        get => InvokeAlive(() => _host.Window.Width);
        set => InvokeAlive(() => _host.Window.Width = value);
    }

    /// <summary>
    /// Window height in device-independent units.
    /// </summary>
    public double Height
    {
        get => InvokeAlive(() => _host.Window.Height);
        set => InvokeAlive(() => _host.Window.Height = value);
    }

    /// <summary>
    /// Left window position in device-independent units.
    /// </summary>
    public double Left
    {
        get => InvokeAlive(() => _host.Window.Left);
        set => InvokeAlive(() => _host.Window.Left = value);
    }

    /// <summary>
    /// Top window position in device-independent units.
    /// </summary>
    public double Top
    {
        get => InvokeAlive(() => _host.Window.Top);
        set => InvokeAlive(() => _host.Window.Top = value);
    }

    /// <summary>
    /// Window title.
    /// </summary>
    public string Title
    {
        get => InvokeAlive(() => _host.Window.Title);
        set => InvokeAlive(() => _host.Window.Title = value);
    }

    /// <summary>
    /// Whether borderless mode is enabled.
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

}
