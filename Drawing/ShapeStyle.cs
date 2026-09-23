using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

/// <summary>Appearance of newly created measurement shapes. Viewer assignment and shape
/// creation copy and freeze brushes; subsequent changes to the source brushes have no effect.</summary>
public sealed record ShapeStyle
{
    public Brush NormalBrush { get; init; } = Brushes.LimeGreen;
    public Brush SelectedBrush { get; init; } = Brushes.Yellow;
    public Brush TextBackground { get; init; } = Brushes.Transparent;
    public Brush PointBrush { get; init; } = Brushes.Red;
    public Brush RegionBrush { get; init; } = Brushes.Cyan;
    internal static ShapeStyle Default { get; } = new();

    internal ShapeStyle Snapshot()
    {
        Dictionary<Brush, Brush>? cache = null;
        return this with
        {
            NormalBrush = DrawingElement.Copy(NormalBrush, ref cache),
            SelectedBrush = DrawingElement.Copy(SelectedBrush, ref cache),
            TextBackground = DrawingElement.Copy(TextBackground, ref cache),
            PointBrush = DrawingElement.Copy(PointBrush, ref cache),
            RegionBrush = DrawingElement.Copy(RegionBrush, ref cache)
        };
    }
}
