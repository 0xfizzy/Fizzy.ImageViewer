using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Hud;

/// <summary>Screen-space text and pixel inspection. Operations dispatch to the viewer STA.</summary>
public interface IViewerHud
{
    /// <summary>右上角 HUD 标签，通过查看器 STA 读写。关闭或释放开始后抛出 ObjectDisposedException。</summary>
    string? HudLabelText { get; set; }

    /// <summary>Enables pixel inspection in the HUD; defaults to true.</summary>
    bool IsPixelInfoEnabled { get; set; }

    /// <summary>
    /// 在 HUD 层创建文本；通过返回句柄的 Update 更新文本和颜色。
    /// </summary>
    HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft,
        double fontSize = 14);
}
