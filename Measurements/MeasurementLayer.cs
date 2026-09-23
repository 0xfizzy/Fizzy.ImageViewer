using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Layer for model-owned measurements. Content is created through measurement tools.</summary>
public sealed class MeasurementLayer : ViewerLayer
{
    private MeasurementContext? _content;

    internal void BindContent(MeasurementContext content)
    {
        if (_content != null) throw new InvalidOperationException("Measurement content is already bound.");
        _content = content;
    }

    internal OverlayLayer Overlay { get; } = new();

    internal MeasurementLayer(ViewerLayers owner)
        : base(owner, "Measurements", 1000, builtIn: true, hitTest: true)
    {
        Overlay.BindTransform(owner.Transform);
        Root.Children.Add(Overlay);
    }

    internal override void ClearContent()
    {
        if (_content != null) _content.ClearMeasurements();
        else Overlay.ClearVisuals();
    }
    internal override void Redraw(bool scaleOnly) => Overlay.UpdateScale(Owner.Scale);
}
