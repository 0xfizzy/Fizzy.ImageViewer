using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Cuda;

internal sealed class CudaPixelSource(ICudaFrameSource source, FrameDescriptor descriptor, ICudaBackend backend) : IFramePixelSource
{
    private async ValueTask<byte[]> Read(int kind, int[]? points, PixelRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[]? result = null;
        await source.ExecuteAsync(scope => result = backend.Query(scope, descriptor, kind, points, region), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result ?? throw new InvalidOperationException("The bridge did not execute the CUDA callback.");
    }
    public async ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
    {
        var result = new PixelSample[coordinates.Length];
        int channels = descriptor.Format.BytesPerPixel();
        for (int offset = 0; offset < coordinates.Length; offset += 65536)
        {
            int count = Math.Min(65536, coordinates.Length - offset);
            var points = new int[count * 2];
            for (int i = 0; i < count; i++) { var p = coordinates.Span[offset + i]; points[i * 2] = p.X; points[i * 2 + 1] = p.Y; }
            var bytes = await Read(0, points, default, ct).ConfigureAwait(false);
            for (int i = 0; i < count; i++)
            {
                int p = i * channels;
                result[offset + i] = channels == 1 ? new(descriptor.Format, bytes[p], 0, 0, 0, 255) :
                    new(descriptor.Format, 0, bytes[p + Channel(0)], bytes[p + 1], bytes[p + Channel(2)], channels == 4 ? bytes[p + 3] : 255);
            }
        }
        return result;
    }
    private int Channel(int semantic) => semantic < 3 && descriptor.Format is FramePixelFormat.Bgr24 or FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32
        ? 2 - semantic : semantic;

    public async ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
    {
        var bytes = await Read(1, null, region, ct).ConfigureAwait(false);
        var result = new ChannelStatistics[descriptor.Format.BytesPerPixel()];
        for (int c = 0; c < result.Length; c++)
        {
            int p = (result.Length == 1 ? 0 : Channel(c)) * 32;
            long count = checked((long)BitConverter.ToUInt64(bytes, p));
            result[c] = new(count, BitConverter.ToUInt64(bytes, p + 8), BitConverter.ToUInt64(bytes, p + 16), (double)BitConverter.ToUInt64(bytes, p + 24) / count);
        }
        return new(descriptor.Format, result);
    }
    public async ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
    {
        var bytes = await Read(2, null, region, ct).ConfigureAwait(false);
        return ImageFrame.TakeOwnership(new(region.Width, region.Height, checked(region.Width * descriptor.Format.BytesPerPixel()), descriptor.Format), bytes, () => { });
    }
}
