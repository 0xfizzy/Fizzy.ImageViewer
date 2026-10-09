namespace Fizzy.ImageViewer.Drawing
{
    /// <summary>How drawing dimensions respond to image zoom; positions remain anchored in image coordinates.</summary>
    public enum OverlayScaleMode
    {
        /// <summary>Geometry, stroke width, text size and offsets scale with the image.</summary>
        ScaleWithImage,
        /// <summary>Geometry scales with the image while stroke width stays fixed in screen DIPs.</summary>
        FixedStroke,
        /// <summary>Visual size and offsets stay fixed in screen DIPs; the anchor remains in image coordinates.</summary>
        FixedSize
    }
}
