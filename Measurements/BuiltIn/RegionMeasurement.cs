using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Frames;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class RegionMeasurement : MeasurementItem, IFrameQueryClient
{
    private QuerySubscription? _subscription;
    public RegionMeasurement(IMeasurementContext context, Point start)
        : base(context, MeasurementGeometry.Rectangle(start, start), Shapes.CreateRectangle(context.Style), Shapes.CreateLabel(start, "", 5, 0, context.Style)) { }
    protected override void OnComplete() => _subscription = Context.Register(this);
    protected override void OnDisposing() => _subscription?.Dispose();
    public void ClearResult() => UpdateText();
    public QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (IsDisposed || !IsComplete) return null;
        var region = Geometry.ToRegion(descriptor);
        if (region.IsEmpty) return null;
        return new RegionStatisticsQueryRequest(new(Id, Geometry.Version), region, stats =>
        {
            var names = stats.Channels.Length == 1 ? new[] { "Gray" } : new[] { "R", "G", "B", "A" };
            Label.Text = $"{region.Width} × {region.Height} px | " + string.Join(" | ", stats.Channels.Select((s, i) =>
                $"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        });
    }
}

