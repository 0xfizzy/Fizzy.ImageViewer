using Fizzy.ImageViewer.Hud;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>Text displayed in the upper-right HUD label.</summary>
    public string? HudLabelText
    {
        get => InvokeAlive(() => _host.Window.HudLayer.Label);
        set => InvokeAlive(() => _host.Window.HudLayer.Label = value);
    }

    /// <summary>Enables pixel inspection in the HUD. Defaults to true; shares state with the context menu.</summary>
    public bool IsPixelInfoEnabled
    {
        get => InvokeAlive(() => _host.PixelInfo.IsEnabled);
        set => InvokeAlive(() => _host.PixelInfo.IsEnabled = value);
    }

    /// <summary>Creates HUD text with fixed layout.</summary>
    public HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
        => _host.Hud.Add(text, brush, anchor, alignment, fontSize);
}
