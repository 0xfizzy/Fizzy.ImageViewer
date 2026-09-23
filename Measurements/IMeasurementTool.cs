using System.Windows;

namespace Fizzy.ImageViewer.Measurements
{
    public interface IMeasurementTool
    {
        string Id { get; }
        string DisplayName { get; }
        // 当用户点击画布时触发
        // point: 图片像素坐标
        // 返回 true 表示工具完成测量
        bool OnClick(Point point, IMeasurementToolContext ctx);

        // 当鼠标移动时触发 (用于动态绘制预览形状)
        void OnMouseMove(Point point, IMeasurementToolContext ctx);

        // 强制取消当前测量
        void Cancel(IMeasurementToolContext ctx);
    }
}
