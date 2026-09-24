using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Layers;

/// <summary>Public layer capabilities and default layer composition.</summary>
public sealed class ViewerLayers
{
    internal LayerCollection Collection { get; }
    public DrawingLayer Markers { get; }
    public MeasurementLayer Measurements { get; }
    public IReadOnlyList<ViewerLayer> Items => Collection.Items;

    internal ViewerLayers(Transform transform, ViewerLifetime? lifetime = null)
    {
        Collection = new(transform, lifetime);
        Markers = Collection.Add(new DrawingLayer(Collection, "Markers", 0, true));
        Measurements = Collection.Add(new MeasurementLayer(Collection));
    }

    /// <summary>Creates a custom image-coordinate drawing layer.</summary>
    public DrawingLayer CreateDrawingLayer(string name) => Collection.Invoke(() =>
        Collection.Add(new DrawingLayer(Collection, name, 100, false)));
    public void RemoveLayer(ViewerLayer layer) => Collection.RemoveLayer(layer);
    /// <summary>Clears all business-layer content while retaining layers and HUD text.</summary>
    public void ClearContents() => Collection.Clear();
}
