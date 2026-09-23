using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer
{
    public static class Shapes
    {
        public const double BaseStrokeThickness = 2.0;
        public const double BaseFontSize = 14.0;

        // 1. 创建线段 (FixedStroke)
        public static Line CreateLine(ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            return new()
            {
                Stroke = style.NormalBrush,
                StrokeThickness = BaseStrokeThickness,
                Tag = new OverlayTagData(OverlayScaleMode.FixedStroke, ShapeType.Line) { OriginalBrush = style.NormalBrush, SelectedBrush = style.SelectedBrush }
            };
        }

        // 2. 创建文字标签 (AnchoredLabel)
        public static TextBlock CreateLabel(Point anchor, string text = "", double offsetX = 10, double offsetY = 10, ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            return new TextBlock
            {
                Text = text,
                Foreground = style.NormalBrush,
                Background = style.TextBackground,
                Padding = new Thickness(3),
                FontSize = BaseFontSize,
                IsHitTestVisible = false, // 标签不可点击，通过主形状选中
                Tag = new OverlayTagData(OverlayScaleMode.AnchoredLabel, ShapeType.Text)
                {
                    AnchorPoint = anchor,
                    ScreenOffset = new Vector(offsetX, offsetY),
                    OriginalBrush = style.NormalBrush,
                    SelectedBrush = style.SelectedBrush
                }
            };
        }

        // 3. 创建点/锚点形状 (FixedSize)
        public static Path CreatePoint(Point position, ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            var geometry = new GeometryGroup();
            double r = 4;
            geometry.Children.Add(new EllipseGeometry(new Point(0, 0), r, r));

            var path = new Path
            {
                Fill = style.PointBrush,
                Data = geometry,
                Tag = new OverlayTagData(OverlayScaleMode.FixedSize, ShapeType.Point)
                {
                    AnchorPoint = position,
                    OriginalBrush = style.PointBrush,
                    SelectedBrush = style.SelectedBrush
                }

            };
            return path;
        }

        // 4. 创建准星 (FixedSize)
        public static Path CreateCrosshair(Point position, double size = 20, double thickness = 2, ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(-size, 0), new Point(size, 0)));
            geometry.Children.Add(new LineGeometry(new Point(0, -size), new Point(0, size)));
            geometry.Children.Add(new EllipseGeometry(new Point(0, 0), size / 2, size / 2));

            var path = new Path
            {
                Stroke = style.NormalBrush,
                StrokeThickness = thickness,
                Data = geometry,
                Tag = new OverlayTagData(OverlayScaleMode.FixedSize, ShapeType.Crosshair)
                {
                    AnchorPoint = position,
                    OriginalBrush = style.NormalBrush,
                    SelectedBrush = style.SelectedBrush
                }
            };
            return path;
        }

        // 5. 创建矩形框 (FixedStroke)
        public static Rectangle CreateRectangle(ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            return new()
            {
                Width = 0, Height = 0,
                Stroke = style.RegionBrush,
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Tag = new OverlayTagData(OverlayScaleMode.FixedStroke, ShapeType.Rectangle) { OriginalBrush = style.RegionBrush, SelectedBrush = style.SelectedBrush }
            };
        }

        // 6. 创建圆形 (FixedStroke)
        public static Path CreateCircle(Point center, double radius, ShapeStyle? style = null)
        {
            style = (style ?? ShapeStyle.Default).Snapshot();
            var geometry = new EllipseGeometry(new Point(0, 0), radius, radius);

            var path = new Path
            {
                Stroke = style.NormalBrush,
                StrokeThickness = BaseStrokeThickness,
                Data = geometry,
                Tag = new OverlayTagData(OverlayScaleMode.FixedStroke, ShapeType.Circle)
                {
                    AnchorPoint = center,
                    OriginalBrush = style.NormalBrush,
                    SelectedBrush = style.SelectedBrush
                }
            };

            Canvas.SetLeft(path, center.X);
            Canvas.SetTop(path, center.Y);
            return path;
        }
    }
}
