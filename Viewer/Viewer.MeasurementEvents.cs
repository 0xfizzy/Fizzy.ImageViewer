using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    /// <summary>Raised once after a measurement completes, on the viewer STA. Previews are excluded.</summary>
    /// <remarks>Subscribers may start a new measurement, including the same tool, before returning.</remarks>
    public event EventHandler<MeasurementEventArgs>? MeasurementCompleted;
    /// <summary>Raised when a previously completed measurement is removed, including clear and closure.</summary>
    public event EventHandler<MeasurementEventArgs>? MeasurementRemoved;

    internal void NotifyMeasurementCompleted(MeasurementItem item) => NotifyMeasurement(MeasurementCompleted, item);
    internal void NotifyMeasurementRemoved(MeasurementItem item) => NotifyMeasurement(MeasurementRemoved, item);

    private void NotifyMeasurement(EventHandler<MeasurementEventArgs>? handlers, MeasurementItem item)
    {
        var geometry = item.Geometry;
        var args = new MeasurementEventArgs(new(item.Id, geometry, item.GeometryVersion),
            new MeasurementRemoval(this, item));
        foreach (EventHandler<MeasurementEventArgs> handler in handlers?.GetInvocationList() ?? [])
            try { handler(this, args); }
            catch (Exception ex) { _logger.LogWarning(ex, "Measurement subscriber failed"); }
    }

    private sealed class MeasurementRemoval(Viewer viewer, MeasurementItem item) : IDisposable
    {
        public void Dispose() => viewer.Layers.InvokeRemoval(item.Dispose);
    }
}
