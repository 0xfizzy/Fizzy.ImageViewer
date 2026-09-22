namespace Fizzy.ImageViewer.Enums;

/// <summary>
/// 指定锚点在 HUD 文本元素上的对齐位置（九宫格）。
/// </summary>
public enum AnchorAlignment
{
    /// <summary>锚点位于文本左上角（默认）。</summary>
    TopLeft,

    /// <summary>锚点位于文本顶部中央。</summary>
    TopCenter,

    /// <summary>锚点位于文本右上角。</summary>
    TopRight,

    /// <summary>锚点位于文本左侧中央。</summary>
    CenterLeft,

    /// <summary>锚点位于文本正中央。</summary>
    Center,

    /// <summary>锚点位于文本右侧中央。</summary>
    CenterRight,

    /// <summary>锚点位于文本左下角。</summary>
    BottomLeft,

    /// <summary>锚点位于文本底部中央。</summary>
    BottomCenter,

    /// <summary>锚点位于文本右下角。</summary>
    BottomRight
}
