using Fizzy.ImageViewer.Interfaces;
using System.Windows;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.MeasureMethods
{
    public class RectMeasure : IMeasureMethod
    {
        public string Name => "ROI";

        private Point? _startPoint;
        private Rectangle? _rect;

        public bool OnClick(Point point, MeasureContext ctx)
        {
            if (_startPoint == null)
            {
                _startPoint = point;
                _rect = Shapes.CreateRectangle();

                ctx.AddShape(_rect);
                UpdateRect(point, point, ctx);
                return false;
            }

            // 完成框选
            UpdateRect(_startPoint.Value, point, ctx);
            _startPoint = null;
            _rect = null;
            return true;
        }

        public void OnMouseMove(Point point, MeasureContext ctx)
        {
            if (_startPoint != null && _rect != null)
            {
                UpdateRect(_startPoint.Value, point, ctx);
            }
        }

        public void Cancel(MeasureContext ctx)
        {
            if (_rect != null) ctx.RemoveShape(_rect);
            _startPoint = null;
            _rect = null;
        }

        private void UpdateRect(Point p1, Point p2, MeasureContext ctx)
        {
            if (_rect == null) return;

            double x = Math.Min(p1.X, p2.X);
            double y = Math.Min(p1.Y, p2.Y);
            double w = Math.Abs(p1.X - p2.X);
            double h = Math.Abs(p1.Y - p2.Y);

            _rect.Width = w;
            _rect.Height = h;
            ctx.UpdateAnchor(_rect, new(x, y));
        }
    }
}
