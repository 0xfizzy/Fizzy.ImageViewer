namespace Fizzy.ImageViewer.Frames;

public readonly record struct ChannelStatistics(long Count, double? Minimum, double? Maximum, double? Mean);
