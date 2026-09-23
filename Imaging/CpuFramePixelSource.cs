using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

internal sealed class CpuFramePixelSource(FrameDescriptor descriptor, ReadOnlyMemory<byte> pixels) : IFramePixelSource
{
    public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
    {
        var result = new PixelSample[coordinates.Length];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            var p = coordinates.Span[i];
            result[i] = FramePixelReader.Decode(descriptor.Format, pixels.Span.Slice(p.Y * descriptor.Stride + p.X * descriptor.Format.BytesPerPixel()));
        }
        return ValueTask.FromResult(result);
    }
    public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
    {
        int channels = descriptor.Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float ? 1 : descriptor.Format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32 ? 4 : 3;
        var count = new long[channels]; var min = Enumerable.Repeat(double.PositiveInfinity, channels).ToArray();
        var max = Enumerable.Repeat(double.NegativeInfinity, channels).ToArray(); var sum = new double[channels];
        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                var p = FramePixelReader.Decode(descriptor.Format, pixels.Span.Slice(y * descriptor.Stride + x * descriptor.Format.BytesPerPixel()));
                for (int c = 0; c < channels; c++)
                {
                    double v = p.IsGrayscale ? p.Gray : c == 0 ? p.R : c == 1 ? p.G : c == 2 ? p.B : p.A;
                    if (!double.IsFinite(v)) continue;
                    count[c]++; min[c] = Math.Min(min[c], v); max[c] = Math.Max(max[c], v); sum[c] += v;
                }
            }
        }
        return ValueTask.FromResult(new RegionStatistics(descriptor.Format, Enumerable.Range(0, channels).Select(c => count[c] == 0 ? new ChannelStatistics(0, null, null, null) : new(count[c], min[c], max[c], sum[c] / count[c])).ToArray()));
    }
    public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
    {
        int stride = checked(region.Width * descriptor.Format.BytesPerPixel());
        var bytes = new byte[checked(stride * region.Height)];
        for (int y = 0; y < region.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            pixels.Span.Slice((region.Y + y) * descriptor.Stride + region.X * descriptor.Format.BytesPerPixel(), stride).CopyTo(bytes.AsSpan(y * stride));
        }
        return ValueTask.FromResult(ImageFrame.TakeOwnership(new(region.Width, region.Height, stride, descriptor.Format), bytes, () => { }));
    }
}
