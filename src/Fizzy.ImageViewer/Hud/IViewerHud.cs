using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Hud;

/// <summary>Screen-space text and pixel inspection. Operations dispatch to the viewer STA.</summary>
public interface IViewerHud
{
    /// <summary>Upper-right HUD label, accessed on the viewer STA. Null becomes empty; empty text hides the label.
    /// Throws ObjectDisposedException after shutdown begins.</summary>
    string? HudLabelText { get; set; }

    /// <summary>Enables pixel inspection in the HUD; defaults to true.</summary>
    bool IsPixelInfoEnabled { get; set; }

    /// <summary>
    /// Creates HUD text; use the returned handle to update its text and color.
    /// </summary>
    HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft,
        double fontSize = 14);
}
