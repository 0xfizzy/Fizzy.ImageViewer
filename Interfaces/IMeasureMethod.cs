using Fizzy.ImageViewer.Controls;
using System.Collections.Generic;
using System.Windows;

namespace Fizzy.ImageViewer.Interfaces
{
    public interface IMeasureMethod
    {
        string Id { get; }
        string DisplayName { get; }
        // 当用户点击画布时触发
        // point: 图片像素坐标
        // isFinished: 方法是否完成测量
        bool OnClick(Point point, MeasureContext ctx);

        // 当鼠标移动时触发 (用于动态绘制预览形状)
        void OnMouseMove(Point point, MeasureContext ctx);

        // 强制取消当前测量
        void Cancel(MeasureContext ctx);
    }
}
