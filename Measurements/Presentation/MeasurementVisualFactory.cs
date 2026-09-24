using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Measurements.Presentation
{
    internal static class MeasurementVisualFactory
    {
        public const double BaseStrokeThickness = 2.0;
        public const double BaseFontSize = 14.0;

        // 1. 创建线段 (FixedStroke)
        public static Line CreateLine(MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var visual = new Line
            {
                Stroke = style.NormalBrush,
                StrokeThickness = BaseStrokeThickness
            };
            MeasurementVisualData.Attach(visual, new MeasurementVisualData(OverlayScaleMode.FixedStroke) { SelectedBrush = style.SelectedBrush });
            return visual;
        }

        // 2. 创建文字标签 (FixedSize)
        public static TextBlock CreateLabel(Point anchor, string text = "", double offsetX = 10, double offsetY = 10, MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var visual = new TextBlock
            {
                Text = text,
                Foreground = style.NormalBrush,
                Background = style.TextBackground,
                Padding = new Thickness(3),
                FontSize = BaseFontSize,
                IsHitTestVisible = false, // 标签不可点击，通过主形状选中
            };
            MeasurementVisualData.Attach(visual, new MeasurementVisualData(OverlayScaleMode.FixedSize)
            {
                AnchorPoint = anchor,
                ScreenOffset = new Vector(offsetX, offsetY),
                SelectedBrush = style.SelectedBrush
            });
            return visual;
        }

        // 3. 创建点/锚点形状 (FixedSize)
        public static Path CreatePoint(Point position, MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var geometry = new GeometryGroup();
            double r = 4;
            geometry.Children.Add(new EllipseGeometry(new Point(0, 0), r, r));

            var path = new Path
            {
                Fill = style.PointBrush,
                Data = geometry
            };
            MeasurementVisualData.Attach(path, new MeasurementVisualData(OverlayScaleMode.FixedSize, usesFill: true)
            {
                AnchorPoint = position,
                SelectedBrush = style.SelectedBrush
            });
            return path;
        }

        // 4. 创建准星 (FixedSize)
        public static Path CreateCrosshair(Point position, double armLength = 20, double thickness = 2, MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(-armLength, 0), new Point(armLength, 0)));
            geometry.Children.Add(new LineGeometry(new Point(0, -armLength), new Point(0, armLength)));
            geometry.Children.Add(new EllipseGeometry(new Point(0, 0), armLength / 2, armLength / 2));

            var path = new Path
            {
                Stroke = style.NormalBrush,
                StrokeThickness = thickness,
                Data = geometry
            };
            MeasurementVisualData.Attach(path, new MeasurementVisualData(OverlayScaleMode.FixedSize)
            {
                AnchorPoint = position,
                SelectedBrush = style.SelectedBrush
            });
            return path;
        }

        // 5. 创建矩形框 (FixedStroke)
        public static Rectangle CreateRectangle(MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var visual = new Rectangle
            {
                Width = 0,
                Height = 0,
                Stroke = style.RegionBrush,
                StrokeThickness = 1.0,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            MeasurementVisualData.Attach(visual, new MeasurementVisualData(OverlayScaleMode.FixedStroke) { SelectedBrush = style.SelectedBrush });
            return visual;
        }

        // 6. 创建圆形 (FixedStroke)
        public static Path CreateCircle(Point center, double radius, MeasurementStyle? style = null)
        {
            style = (style ?? MeasurementStyle.Default).Snapshot();
            var geometry = new EllipseGeometry(new Point(0, 0), radius, radius);

            var path = new Path
            {
                Stroke = style.NormalBrush,
                StrokeThickness = BaseStrokeThickness,
                Data = geometry
            };
            MeasurementVisualData.Attach(path, new MeasurementVisualData(OverlayScaleMode.FixedStroke)
            {
                AnchorPoint = center,
                SelectedBrush = style.SelectedBrush
            });

            Canvas.SetLeft(path, center.X);
            Canvas.SetTop(path, center.Y);
            return path;
        }
    }
}
