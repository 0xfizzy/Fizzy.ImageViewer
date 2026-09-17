namespace Fizzy.ImageViewer.Constants;

/// <summary>
/// 渲染相关的常量配置。
/// </summary>
public static class RenderingConstants
{
    /// <summary>
    /// 默认渲染队列最大深度。
    /// 1 = 纯跳帧（最低延迟），3 = 默认（平衡），更大 = 更流畅但延迟更高。
    /// </summary>
    public const int DefaultMaxRenderQueue = 3;

    /// <summary>
    /// 渲染队列最小深度。
    /// </summary>
    public const int MinRenderQueue = 1;

    /// <summary>
    /// 默认 DPI 值。
    /// </summary>
    public const double DefaultDpi = 96.0;
}
