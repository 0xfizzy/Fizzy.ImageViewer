using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Layer for model-owned measurements. Content is created through measurement tools.</summary>
public sealed class MeasurementLayer : ViewerLayer
{
    internal OverlayLayer Overlay { get; } = new();

    internal MeasurementLayer(ViewerLayers owner)
        : base(owner, "Measurements", 1000, builtIn: true, hitTest: true)
    {
        Overlay.BindTransform(owner.Transform);
        Root.Children.Add(Overlay);
    }

    internal override void ClearContent() => Overlay.ClearVisuals();
    internal override void Redraw(bool scaleOnly) => Overlay.UpdateScale(Owner.Scale);
}
