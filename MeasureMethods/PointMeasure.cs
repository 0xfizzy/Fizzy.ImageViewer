using Fizzy.ImageViewer.Interfaces;
using System.Windows;

namespace Fizzy.ImageViewer.MeasureMethods
{
    public class PointMeasure : IMeasureMethod
    {
        public string Name => "Point";

        public bool OnClick(Point point, MeasureContext ctx)
        {
            // 1. 创建点形状 (FixedSize)
            var shape = Shapes.CreatePoint(point);
            ctx.AddShape(shape);

            // 2. 创建标签 (AnchoredLabel)，显示亚像素坐标
            var label = Shapes.CreateLabel(point, $"X:{point.X:F2}\nY:{point.Y:F2}", 10, -20);
            ctx.AddShape(label);

            // 设置关联形状
            if (shape.Tag is OverlayTagData tagData)
            {
                tagData.LinkedShapes = [label];
            }

            return true; // 点击即完成
        }

        public void OnMouseMove(Point point, MeasureContext ctx) { }
        public void Cancel(MeasureContext ctx) { }
    }
}