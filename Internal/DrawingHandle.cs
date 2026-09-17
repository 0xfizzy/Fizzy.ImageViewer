using System;
using System.Windows;

namespace Fizzy.ImageViewer.Internal
{
    // 内部类：用于封装绘图对象的生命周期
    internal class DrawingHandle(Action disposeAction) : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// 可选的关联对象引用，用于在后续操作中访问底层元素（如 TextBlock）。
        /// </summary>
        internal object? State { get; set; }

        public void Dispose()
        {
            if (!_disposed)
            {
                disposeAction?.Invoke();
                _disposed = true;
            }
        }
    }
}