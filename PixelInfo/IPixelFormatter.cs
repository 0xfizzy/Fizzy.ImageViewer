using System.Windows.Media;

namespace Fizzy.ImageViewer.PixelInfo;

/// <summary>
/// 像素格式化器接口，用于扩展支持更多像素格式。
/// </summary>
public interface IPixelFormatter
{
    /// <summary>
    /// 检查是否支持指定的像素格式。
    /// </summary>
    bool CanFormat(PixelFormat format);

    /// <summary>
    /// 从原始像素数据读取像素值。
    /// </summary>
    /// <param name="pixelData">像素原始字节数据</param>
    /// <param name="value">输出的像素值</param>
    /// <returns>是否成功读取</returns>
    bool TryRead(ReadOnlySpan<byte> pixelData, out PixelValue value);
}
