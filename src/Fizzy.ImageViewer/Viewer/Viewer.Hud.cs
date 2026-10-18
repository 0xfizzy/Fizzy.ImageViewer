using Fizzy.ImageViewer.Hud;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>Text displayed in the upper-right HUD label. Null is normalized to an empty string;
    /// an empty string hides the label.</summary>
    public string? HudLabelText
    {
        get => _host.Control.HudLabelText;
        set => _host.Control.HudLabelText = value;
    }

    /// <summary>Enables pixel inspection in the HUD. Defaults to true; shares state with the context menu.</summary>
    public bool IsPixelInfoEnabled
    {
        get => _host.Control.IsPixelInfoEnabled;
        set => _host.Control.IsPixelInfoEnabled = value;
    }

    /// <summary>Creates HUD text with fixed layout.</summary>
    public HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
        => _host.Control.DrawHudText(text, brush, anchor, alignment, fontSize);
}
