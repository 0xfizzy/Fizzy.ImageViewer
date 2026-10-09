namespace Fizzy.ImageViewer.Frames;

/// <summary>Statistics of finite original values for one semantic channel.</summary>
/// <param name="Count">Number of finite samples included.</param>
/// <param name="Minimum">Smallest finite value, or null when count is zero.</param>
/// <param name="Maximum">Largest finite value, or null when count is zero.</param>
/// <param name="Mean">Arithmetic mean of finite values, or null when count is zero.</param>
public readonly record struct ChannelStatistics(long Count, double? Minimum, double? Maximum, double? Mean);
