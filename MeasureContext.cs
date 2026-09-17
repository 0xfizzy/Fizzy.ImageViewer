using Fizzy.ImageViewer.Controls;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Fizzy.ImageViewer
{
    public class MeasureContext(OverlayLayer layer)
    {
        private WriteableBitmap? _currentBitmap;

        /// <summary>
        /// 当前显示的图像。
        /// </summary>
        public WriteableBitmap? CurrentBitmap => _currentBitmap;

        /// <summary>
        /// 图像更新时触发。参数为新的 WriteableBitmap。
        /// </summary>
        public event Action<WriteableBitmap>? ImageUpdated;

        public void AddShape(UIElement shape) => layer.AddShape(shape);
        public void RemoveShape(UIElement shape) => layer.RemoveShape(shape);
        public void UpdateAnchor(UIElement shape, Point newAnchor) => layer.UpdateAnchor(shape, newAnchor);

        /// <summary>
        /// 更新当前图像并触发事件。由 Viewer 调用。
        /// </summary>
        internal void NotifyImageUpdated(WriteableBitmap bitmap)
        {
            _currentBitmap = bitmap;
            ImageUpdated?.Invoke(bitmap);
        }
    }
}
