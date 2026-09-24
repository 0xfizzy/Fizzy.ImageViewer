namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class LineProfileTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.LineProfile;
    public string DisplayName => "Line profile";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new TwoPointCreationSession(context, MeasurementGeometry.Line, new() { Query = MeasurementQuery.LineProfile, ShowProfileWindow = true });
}
