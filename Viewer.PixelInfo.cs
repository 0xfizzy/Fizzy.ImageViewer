using Fizzy.ImageViewer.MenuItems;
using Fizzy.ImageViewer.PixelInfo;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private PixelInfoOverlay? _pixelInfoOverlay;

    // 内置格式化器（按优先级排序）
    private static readonly IPixelFormatter[] BuiltInFormatters =
    [
        Gray8Formatter.Instance,
        Bgr24Formatter.Instance,
        Rgb24Formatter.Instance,
        Bgr32Formatter.Instance
    ];

    /// <summary>
    /// 初始化像素信息叠加层功能。
    /// 在 UI 线程调用。
    /// </summary>
    private void InitializePixelInfoOverlay(ViewerWindow win, Managers.MenuManager menuMgr)
    {
        _pixelInfoOverlay = new PixelInfoOverlay(
            win.Layer0,
            win.Layer2,
            TryReadPixelInternal);

        // 默认启用
        _pixelInfoOverlay.Enable();

        // 注册菜单项 - 使用简化的 CheckableMenuItem
        menuMgr.Register(new CheckableMenuItem(
            "Pixel Info",
            () => _pixelInfoOverlay.IsEnabled,
            () => { if (_pixelInfoOverlay.IsEnabled) _pixelInfoOverlay.Disable(); else _pixelInfoOverlay.Enable(); }
        ));
    }

    /// <summary>
    /// 尝试读取指定位置的像素值。
    /// 内部方法，在 UI 线程调用，0-GC 实现。
    /// </summary>
    private bool TryReadPixelInternal(int x, int y, out PixelValue value)
    {
        value = default;

        if (_wBitmap == null) return false;

        int width = _wBitmap.PixelWidth;
        int height = _wBitmap.PixelHeight;

        // 边界检查
        if (x < 0 || x >= width || y < 0 || y >= height)
            return false;

        var format = _wBitmap.Format;
        int bytesPerPixel = (format.BitsPerPixel + 7) / 8;
        int stride = _wBitmap.BackBufferStride;

        // 查找合适的格式化器（使用 for 循环避免 foreach 的枚举器分配）
        IPixelFormatter? formatter = null;
        for (int i = 0; i < BuiltInFormatters.Length; i++)
        {
            if (BuiltInFormatters[i].CanFormat(format))
            {
                formatter = BuiltInFormatters[i];
                break;
            }
        }

        if (formatter == null) return false;

        // 读取像素数据
        _wBitmap.Lock();
        try
        {
            unsafe
            {
                byte* backBuffer = (byte*)_wBitmap.BackBuffer;
                byte* pixel = backBuffer + y * stride + x * bytesPerPixel;

                // 创建 Span 读取像素
                var pixelSpan = new ReadOnlySpan<byte>(pixel, bytesPerPixel);

                return formatter.TryRead(pixelSpan, out value);
            }
        }
        finally
        {
            _wBitmap.Unlock();
        }
    }
}
