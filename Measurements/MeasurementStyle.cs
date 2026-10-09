using System.Windows.Media;

using Fizzy.ImageViewer.Drawing;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Appearance of newly created measurement shapes. Viewer assignment and shape
/// creation copy and freeze brushes; subsequent changes to the source brushes have no effect.</summary>
public sealed record MeasurementStyle
{
    /// <summary>Unselected line, crosshair and circle outlines and label foreground; defaults to LimeGreen.</summary>
    public Brush NormalBrush { get; init; } = Brushes.LimeGreen;
    /// <summary>Selected shape outline or point fill; defaults to Yellow.</summary>
    public Brush SelectedBrush { get; init; } = Brushes.Yellow;
    /// <summary>Measurement label background; defaults to Transparent.</summary>
    public Brush TextBackground { get; init; } = Brushes.Transparent;
    /// <summary>Point marker fill; defaults to Red.</summary>
    public Brush PointBrush { get; init; } = Brushes.Red;
    /// <summary>Unselected rectangle outline; defaults to Cyan.</summary>
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
