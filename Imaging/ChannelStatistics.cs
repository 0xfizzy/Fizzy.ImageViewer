namespace Fizzy.ImageViewer.Imaging;

public readonly record struct ChannelStatistics(long Count, double? Minimum, double? Maximum, double? Mean);
