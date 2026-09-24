namespace Fizzy.ImageViewer.Drawing
{
    public enum OverlayScaleMode
    {
        ScaleWithImage,           // 普通跟随（如测量线本身）
        FixedStroke,    // 锁定线宽（如矩形框）
        FixedSize,      // 锁定整体大小（如准星、点形状）
        AnchoredLabel   // 锁定字号 + 锁定与锚点的屏幕距离（如文字标签）
    }
}
