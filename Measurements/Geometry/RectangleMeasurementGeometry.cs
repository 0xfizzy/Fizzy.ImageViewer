using System.Windows;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements;

public sealed record RectangleMeasurementGeometry : MeasurementGeometry
{
    public Point Start { get; }
    public Point End { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Rectangle;
    public override Rect Bounds => new(Start, End);
    internal override Point Anchor => Start;
    internal override IReadOnlyList<Point> ControlPoints => [Start, new(End.X, Start.Y), End, new(Start.X, End.Y)];
    internal RectangleMeasurementGeometry(Point a, Point b)
    {
        EnsureExtent(a, b);
        Start = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        End = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
    internal PixelRegion ToRegion(FrameDescriptor descriptor) =>
        PixelRegion.Clip(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, descriptor);
    // The opposite corner belongs to the drag-start snapshot, even after crossing it.
    internal override MeasurementGeometry MoveControlPoint(int index, Point point) => index is >= 0 and < 4
        ? Rectangle(ControlPoints[(index + 2) % 4], point) : throw new ArgumentOutOfRangeException(nameof(index));
}
