namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class RectangleRoiTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.RectangleRoi;
    public string DisplayName => "Rectangle ROI";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new TwoPointCreationSession(context, MeasurementGeometry.Rectangle, new() { Query = MeasurementQuery.RegionStatistics });
}
