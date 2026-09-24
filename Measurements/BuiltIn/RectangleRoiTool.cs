namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class RectangleRoiTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.ROI;
    public string DisplayName => "ROI";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new TwoPointCreationSession(context, MeasurementGeometry.Rectangle, new() { Query = MeasurementQuery.RegionStatistics });
}
