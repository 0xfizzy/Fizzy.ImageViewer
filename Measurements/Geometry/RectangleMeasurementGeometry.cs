using System.Windows;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

public sealed record RectangleMeasurementGeometry : MeasurementGeometry
{
    public Point TopLeft { get; }
    public Point BottomRight { get; }
    public override MeasurementGeometryKind Kind => MeasurementGeometryKind.Rectangle;
    public override Rect Bounds => new(TopLeft, BottomRight);
    internal override Point Anchor => TopLeft;
    internal override IReadOnlyList<Point> ControlPoints => [TopLeft, new(BottomRight.X, TopLeft.Y), BottomRight, new(TopLeft.X, BottomRight.Y)];
    internal RectangleMeasurementGeometry(Point a, Point b)
    {
        EnsureExtent(a, b);
        TopLeft = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        BottomRight = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
    internal PixelRegion ToRegion(FrameDescriptor descriptor) =>
        PixelRegion.Clip(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, descriptor);
    // The opposite corner belongs to the drag-start snapshot, even after crossing it.
    internal override MeasurementGeometry MoveControlPoint(int index, Point point) => index is >= 0 and < 4
        ? Rectangle(ControlPoints[(index + 2) % 4], point) : throw new ArgumentOutOfRangeException(nameof(index));
}
