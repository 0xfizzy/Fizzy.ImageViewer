namespace Fizzy.ImageViewer.Measurements.BuiltIn;
internal sealed class LineStrengthTool : LineTool
{
    public override string Id => MeasurementToolIds.LineStrength;
    public override string DisplayName => "Line strength";
    protected override MeasurementOptions Options => new() { Query = MeasurementQuery.LineProfile, ShowLineProfile = true };
}
