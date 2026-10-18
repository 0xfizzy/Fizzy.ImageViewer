using Fizzy.ImageViewer.Hud;
using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <summary>Text displayed in the upper-right HUD label. Null is normalized to an empty string;
    /// an empty string hides the label.</summary>
    public string? HudLabelText
    {
        get => InvokeAlive(() => HudLayer.Label);
        set => InvokeAlive(() => HudLayer.Label = value);
    }

    /// <summary>Enables pixel inspection in the HUD. Defaults to true; shares state with the context menu.</summary>
    public bool IsPixelInfoEnabled
    {
        get => InvokeAlive(() => _runtime.PixelInfo.IsEnabled);
        set => InvokeAlive(() => _runtime.PixelInfo.IsEnabled = value);
    }

    /// <summary>Creates HUD text with fixed layout.</summary>
    public HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
        => _runtime.Hud.Add(text, brush, anchor, alignment, fontSize);
}
