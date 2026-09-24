namespace Fizzy.ImageViewer.Imaging;

public readonly record struct PixelQueryMetrics(long Batches, long ExpiredResults, double LastDurationMilliseconds);
