using Fizzy.ImageViewer.Frames;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.Methods;

internal sealed class RegionMeasurement : MeasurementItem
{
    public RegionMeasurement(IMeasurementContext context, Point start)
        : base(context, MeasurementGeometry.Rectangle(start, start), Shapes.CreateRectangle(context.Style), Shapes.CreateLabel(start, "绛夊緟鏁版嵁", 5, 0, context.Style)) { }
    protected override void OnComplete() => Subscribe();
    public override QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (IsDisposed || !IsComplete) return null;
        var region = Geometry.ToRegion(descriptor);
        if (region.IsEmpty) return null;
        return new RegionStatisticsQueryRequest(new(Id, Geometry.Version), region, stats =>
        {
            Result = stats;
            var names = stats.Channels.Length == 1 ? new[] { "Gray" } : new[] { "R", "G", "B", "A" };
            Label.Text = $"{region.Width}脳{region.Height} px | " + string.Join(" | ", stats.Channels.Select((s, i) =>
                $"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        });
    }
}

