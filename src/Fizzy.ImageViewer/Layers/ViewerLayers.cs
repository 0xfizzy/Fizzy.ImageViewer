using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Measurements;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer.Layers;

/// <summary>Public layer capabilities and default layer composition.</summary>
public sealed class ViewerLayers
{
    internal LayerCollection Collection { get; }
    /// <summary>Built-in default drawing layer used by the viewer drawing helpers.</summary>
    public DrawingLayer Markers { get; }
    /// <summary>Built-in layer for measurement previews and completed items.</summary>
    public MeasurementLayer Measurements { get; }
    /// <summary>A membership snapshot containing live, mutable layer handles.</summary>
    public IReadOnlyList<ViewerLayer> Items => Collection.Items;

    internal ViewerLayers(Transform transform, ViewerLifetime? lifetime = null, ILogger? logger = null)
    {
        Collection = new(transform, lifetime, logger);
        Markers = Collection.Add(new DrawingLayer(Collection, "Markers", 0, true));
        Measurements = Collection.Add(new MeasurementLayer(Collection));
    }

    /// <summary>Creates a custom image-coordinate drawing layer.</summary>
    public DrawingLayer CreateDrawingLayer(string name) => Collection.Invoke(() =>
        Collection.Add(new DrawingLayer(Collection, name, 100, false)));
    /// <summary>Removes and clears a custom layer owned by this viewer; built-in layers and layers from another viewer are rejected.</summary>
    public void RemoveLayer(ViewerLayer layer) => Collection.RemoveLayer(layer);
    /// <summary>Clears all business-layer content while retaining layers and HUD text.</summary>
    public void ClearContents() => Collection.Clear();
}
