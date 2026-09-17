using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fizzy.ImageViewer.Interfaces;

/// <summary>
/// 图像源抽象接口，用于 0-GC 热路径渲染。
/// <para>
/// 引用计数机制说明：
/// <list type="bullet">
/// <item><see cref="Retain"/> - 在图像数据被提交到渲染队列前调用，防止数据在渲染完成前被释放</item>
/// <item><see cref="Release"/> - 在渲染完成后调用，允许底层数据被回收或重用</item>
/// </list>
/// 如果底层数据不需要引用计数（如不可变数据），可以使用默认的空实现。
/// </para>
/// </summary>
public interface IImageSource : IDisposable
{
    /// <summary>
    /// 图像宽度（像素）。
    /// </summary>
    int Width { get; }
    
    /// <summary>
    /// 图像高度（像素）。
    /// </summary>
    int Height { get; }
    
    /// <summary>
    /// WPF 像素格式。
    /// </summary>
    PixelFormat WpfFormat { get; }

    /// <summary>
    /// 将像素数据写入目标 WriteableBitmap。
    /// <para>
    /// 此方法在 UI 线程调用，实现应直接操作 BackBuffer 以获得最佳性能。
    /// 典型实现流程：
    /// <code>
    /// dst.Lock();
    /// try {
    ///     // 复制数据到 dst.BackBuffer
    ///     dst.AddDirtyRect(new Int32Rect(0, 0, dst.PixelWidth, dst.PixelHeight));
    /// } finally {
    ///     dst.Unlock();
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="destination">目标 WriteableBitmap，尺寸和格式已匹配</param>
    void WriteTo(WriteableBitmap destination);
}
