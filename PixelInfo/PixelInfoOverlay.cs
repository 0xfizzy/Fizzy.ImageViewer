using Fizzy.ImageViewer.Controls;
using System.Diagnostics;
using System.Windows.Input;

namespace Fizzy.ImageViewer.PixelInfo;

/// <summary>
/// 像素读取委托，使用 out 参数避免 nullable struct 装箱。
/// </summary>
/// <returns>true 表示读取成功</returns>
public delegate bool TryReadPixelDelegate(int x, int y, out PixelValue value);

/// <summary>
/// 像素信息叠加层管理器。
/// 负责鼠标跟踪、像素读取、HUD 更新，支持 0-GC 热路径。
/// </summary>
public sealed class PixelInfoOverlay
{
    private const int MaxFormatBufferSize = 96; // "X: 99999.99, Y: 99999.99 | R: 255, G: 255, B: 255"

    // 预计算节流间隔（避免每帧计算）
    private static readonly long ThrottleIntervalStopwatchTicks = Stopwatch.Frequency / 30; // 30Hz

    private readonly ImageLayer _imageLayer;
    private readonly HudLayer _hudLayer;
    private readonly TryReadPixelDelegate _pixelReader;

    // 状态
    private bool _isEnabled;
    private long _lastUpdateTicks;

    // 预分配缓冲区，避免每帧分配
    private readonly char[] _formatBuffer = new char[MaxFormatBufferSize];

    // 事件处理器缓存，避免每次订阅/取消时创建委托
    private readonly Action<double, double> _mouseMoveHandler;
    private readonly MouseEventHandler _mouseLeaveHandler;

    public bool IsEnabled => _isEnabled;

    public PixelInfoOverlay(
        ImageLayer imageLayer,
        HudLayer hudLayer,
        TryReadPixelDelegate pixelReader)
    {
        _imageLayer = imageLayer;
        _hudLayer = hudLayer;
        _pixelReader = pixelReader;

        // 预创建委托，避免后续分配
        _mouseMoveHandler = OnImageMouseMove;
        _mouseLeaveHandler = OnMouseLeave;
    }

    /// <summary>
    /// 启用像素信息显示。
    /// </summary>
    public void Enable()
    {
        if (_isEnabled) return;
        _isEnabled = true;

        _imageLayer.ImageMouseMove += _mouseMoveHandler;
        _imageLayer.Container.MouseLeave += _mouseLeaveHandler;
    }

    /// <summary>
    /// 禁用像素信息显示，取消所有事件订阅。
    /// </summary>
    public void Disable()
    {
        if (!_isEnabled) return;
        _isEnabled = false;

        _imageLayer.ImageMouseMove -= _mouseMoveHandler;
        _imageLayer.Container.MouseLeave -= _mouseLeaveHandler;

        // 隐藏显示
        _hudLayer.SetPixelInfoVisible(false);
    }

    private void OnImageMouseMove(double imageX, double imageY)
    {
        // 30Hz 节流（使用预计算的间隔）
        long now = Stopwatch.GetTimestamp();
        if (now - _lastUpdateTicks < ThrottleIntervalStopwatchTicks)
            return;
        _lastUpdateTicks = now;

        int ix = (int)imageX;
        int iy = (int)imageY;

        // 读取像素（使用 out 参数避免 nullable struct 装箱，索引必须为整数）
        if (!_pixelReader(ix, iy, out var pixelValue))
        {
            _hudLayer.SetPixelInfoVisible(false);
            return;
        }

        // 格式化到预分配缓冲区（使用原始 double 坐标保留亚像素精度）
        int len = FormatPixelInfo(imageX, imageY, pixelValue, _formatBuffer);

        // 更新 HUD（传递 Span 避免 string 分配）
        _hudLayer.UpdatePixelInfo(_formatBuffer.AsSpan(0, len));
        _hudLayer.SetPixelInfoVisible(true);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hudLayer.SetPixelInfoVisible(false);
    }

    /// <summary>
    /// 格式化像素信息，0-GC 实现。
    /// 格式: "X: 123.45, Y: 678.90 | R: 255, G: 128, B: 64"
    /// </summary>
    private static int FormatPixelInfo(double x, double y, PixelValue pixel, Span<char> buffer)
    {
        int pos = 0;

        // "X: "
        ReadOnlySpan<char> xPrefix = "X: ";
        xPrefix.CopyTo(buffer[pos..]);
        pos += xPrefix.Length;
        pos += FormatDouble2(x, buffer[pos..]);

        // ", Y: "
        ReadOnlySpan<char> yPrefix = ", Y: ";
        yPrefix.CopyTo(buffer[pos..]);
        pos += yPrefix.Length;
        pos += FormatDouble2(y, buffer[pos..]);

        // " | "
        ReadOnlySpan<char> separator = " | ";
        separator.CopyTo(buffer[pos..]);
        pos += separator.Length;

        // 像素值
        pos += pixel.FormatTo(buffer[pos..]);

        return pos;
    }

    /// <summary>
    /// 格式化 double 值为 2 位小数，0-GC 实现。
    /// </summary>
    private static int FormatDouble2(double value, Span<char> buffer)
    {
        int pos = 0;
        if (value < 0)
        {
            buffer[pos++] = '-';
            value = -value;
        }

        // 先四舍五入到 2 位小数再拆分整数/小数部分，避免进位问题
        long scaled = (long)(value * 100 + 0.5);
        long intPart = scaled / 100;
        int fracPart = (int)(scaled % 100);

        pos += FormatUInt((uint)intPart, buffer[pos..]);
        buffer[pos++] = '.';
        buffer[pos++] = (char)('0' + fracPart / 10);
        buffer[pos++] = (char)('0' + fracPart % 10);

        return pos;
    }

    private static int FormatInt(int value, Span<char> buffer)
    {
        if (value < 0)
        {
            buffer[0] = '-';
            return 1 + FormatUInt((uint)(-value), buffer[1..]);
        }
        return FormatUInt((uint)value, buffer);
    }

    private static int FormatUInt(uint value, Span<char> buffer)
    {
        if (value == 0)
        {
            buffer[0] = '0';
            return 1;
        }

        // 计算位数
        int digits = 0;
        uint temp = value;
        while (temp > 0)
        {
            digits++;
            temp /= 10;
        }

        // 从后往前填充
        for (int i = digits - 1; i >= 0; i--)
        {
            buffer[i] = (char)('0' + value % 10);
            value /= 10;
        }

        return digits;
    }
}
