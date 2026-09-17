using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.MeasureMethods
{
    public class LineMeasure : IMeasureMethod
    {
        public string Name => "Length";

        private Point? _startPoint;
        private Line? _currentLine;
        private TextBlock? _currentLabel;

        public bool OnClick(Point point, MeasureContext ctx)
        {
            if (_startPoint == null)
            {
                // --- 第一步：点击起点 ---
                _startPoint = point;

                // 1. 创建线 (使用 Shapes 工厂)
                _currentLine = Shapes.CreateLine();
                _currentLine.X1 = point.X;
                _currentLine.Y1 = point.Y;
                _currentLine.X2 = point.X;
                _currentLine.Y2 = point.Y;
                ctx.AddShape(_currentLine);

                // 2. 创建标签
                _currentLabel = Shapes.CreateLabel(point, "0.0 px", 5, 0);
                ctx.AddShape(_currentLabel);

                return false; // 继续测量
            }

            // --- 第二步：点击终点 ---
            UpdateShape(point, ctx);

            // 重新添加线段以触发 ShapeAdded 事件（首次添加时起终点相同，属于预览）
            ctx.RemoveShape(_currentLine!);
            ctx.AddShape(_currentLine!);

            // 设置关联形状（选中线段时一起高亮/删除标签）
            if (_currentLine!.Tag is OverlayTagData tagData)
            {
                tagData.LinkedShapes = [_currentLabel!];
            }

            Reset();
            return true; // 结束测量
        }

        public void OnMouseMove(Point point, MeasureContext ctx)
        {
            if (_startPoint != null)
            {
                UpdateShape(point, ctx);
            }
        }

        public void Cancel(MeasureContext ctx)
        {
            if (_currentLine != null) ctx.RemoveShape(_currentLine);
            if (_currentLabel != null) ctx.RemoveShape(_currentLabel);
            Reset();
        }

        private void UpdateShape(Point endPoint, MeasureContext ctx)
        {
            if (_currentLine == null || _currentLabel == null || _startPoint == null) return;

            // 1. 更新线段几何 (这是 FixedStroke，只需改坐标)
            _currentLine.X2 = endPoint.X;
            _currentLine.Y2 = endPoint.Y;

            // 2. 计算距离
            double dist = Math.Sqrt(Math.Pow(endPoint.X - _startPoint.Value.X, 2) +
                                    Math.Pow(endPoint.Y - _startPoint.Value.Y, 2));
            _currentLabel.Text = $"{dist:F1} px";

            ctx.UpdateAnchor(_currentLabel, endPoint);
        }

        private void Reset()
        {
            _startPoint = null;
            _currentLine = null;
            _currentLabel = null;
        }
    }
}
