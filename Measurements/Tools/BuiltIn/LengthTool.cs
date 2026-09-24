namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class LengthTool : IMeasurementTool
{
    public string Id => MeasurementToolIds.Length;
    public string DisplayName => "Length";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new TwoPointCreationSession(context, MeasurementGeometry.Line, new());
}
