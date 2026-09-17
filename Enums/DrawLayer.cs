namespace Fizzy.ImageViewer.Enums;

/// <summary>
/// 指定形状绘制的目标层。
/// </summary>
public enum DrawLayer
{
    /// <summary>
    /// Overlay 层（图像坐标，随图像缩放平移）。
    /// </summary>
    Overlay,

    /// <summary>
    /// HUD 层（屏幕坐标，不随图像缩放平移）。
    /// </summary>
    Hud
}
