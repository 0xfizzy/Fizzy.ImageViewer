using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Layer for model-owned measurements. Content is created through measurement tools.</summary>
public sealed class MeasurementLayer : ViewerLayer
{
    internal event Action? ContentClearing;

    internal MeasurementOverlay Overlay { get; } = new();

    internal MeasurementLayer(LayerCollection owner)
        : base(owner, "Measurements", 1000, builtIn: true, hitTest: true)
    {
        Overlay.BindTransform(owner.Transform);
        Root.Children.Add(Overlay);
    }

    internal override void ClearContent()
    {
        try { ContentClearing?.Invoke(); }
        finally { Overlay.ClearVisuals(); }
    }
    internal override void ReleaseHandlers() => ContentClearing = null;
    internal override void Redraw(bool scaleOnly) => Overlay.UpdateScale(Owner.Scale);
}
