using System.Windows.Media;

using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Appearance of newly created measurement shapes. Viewer assignment and shape
/// creation copy and freeze brushes; subsequent changes to the source brushes have no effect.</summary>
public sealed record MeasurementStyle
{
    public Brush NormalBrush { get; init; } = Brushes.LimeGreen;
    public Brush SelectedBrush { get; init; } = Brushes.Yellow;
    public Brush TextBackground { get; init; } = Brushes.Transparent;
    public Brush PointBrush { get; init; } = Brushes.Red;
    public Brush RegionBrush { get; init; } = Brushes.Cyan;
    internal static MeasurementStyle Default { get; } = new();

    internal MeasurementStyle Snapshot()
    {
        Dictionary<Brush, Brush>? cache = null;
        return this with
        {
            NormalBrush = BrushSnapshots.Copy(NormalBrush, ref cache),
            SelectedBrush = BrushSnapshots.Copy(SelectedBrush, ref cache),
            TextBackground = BrushSnapshots.Copy(TextBackground, ref cache),
            PointBrush = BrushSnapshots.Copy(PointBrush, ref cache),
            RegionBrush = BrushSnapshots.Copy(RegionBrush, ref cache)
        };
    }
}
