namespace Fizzy.ImageViewer.Measurements.BuiltIn;
internal sealed class LineProfileTool : LengthTool
{
    public override string Id => MeasurementToolIds.LineProfile;
    public override string DisplayName => "Line profile";
    protected override MeasurementOptions Options => new() { Query = MeasurementQuery.LineProfile, ShowLineProfile = true };
}
