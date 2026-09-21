using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public static PixelRegion Full(FrameDescriptor d) => new(0, 0, d.Width, d.Height);
    public void Validate(FrameDescriptor d)
    {
        if (IsEmpty || X < 0 || Y < 0 || (long)X + Width > d.Width || (long)Y + Height > d.Height)
            throw new ArgumentOutOfRangeException(nameof(PixelRegion));
    }
    public static PixelRegion Clip(double x, double y, double width, double height, FrameDescriptor d)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return default;
        int left = (int)Math.Clamp(Math.Floor(x), 0, d.Width), top = (int)Math.Clamp(Math.Floor(y), 0, d.Height);
        int right = (int)Math.Clamp(Math.Ceiling(x + width), 0, d.Width), bottom = (int)Math.Clamp(Math.Ceiling(y + height), 0, d.Height);
        return new(left, top, Math.Max(0, right-left), Math.Max(0, bottom-top));
    }
}
public readonly record struct ChannelStatistics(long Count, double? Minimum, double? Maximum, double? Mean);
public sealed record RegionStatistics(FramePixelFormat Format, ChannelStatistics[] Channels);
public sealed record PixelQueryResult(FrameInfo Frame, PixelSample[] Samples);
public sealed record RegionStatisticsResult(FrameInfo Frame, PixelRegion Region, RegionStatistics Statistics);

/// <summary>All operations read original pixels. No operation may silently materialize the full image.</summary>
public interface IFramePixelSource
{
    ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct);
    ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct);
    ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct);
}

public sealed class RegionPixels(FrameInfo frame, PixelRegion region, ImageFrame pixels) : IDisposable
{
    public FrameInfo Frame { get; } = frame;
    public PixelRegion Region { get; } = region;
    public FrameLease AcquirePixels()
    {
        var lease = pixels.Acquire();
        lease.Info = Frame;
        return lease;
    }
    public void Dispose() => pixels.Dispose();
}

internal sealed class CpuFramePixelSource(FrameDescriptor descriptor, ReadOnlyMemory<byte> pixels) : IFramePixelSource
{
    public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct)
    {
        var result = new PixelSample[coordinates.Length];
        for (int i=0;i<result.Length;i++)
        {
            if ((i & 255)==0) ct.ThrowIfCancellationRequested();
            var p=coordinates.Span[i];
            result[i]=FramePixelReader.Decode(descriptor.Format, pixels.Span.Slice(p.Y*descriptor.Stride+p.X*descriptor.Format.BytesPerPixel()));
        }
        return ValueTask.FromResult(result);
    }
    public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct)
    {
        int channels = descriptor.Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float ? 1 : descriptor.Format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32 ? 4 : 3;
        var count=new long[channels]; var min=Enumerable.Repeat(double.PositiveInfinity,channels).ToArray();
        var max=Enumerable.Repeat(double.NegativeInfinity,channels).ToArray(); var sum=new double[channels];
        for (int y=region.Y;y<region.Y+region.Height;y++)
        {
            ct.ThrowIfCancellationRequested();
            for(int x=region.X;x<region.X+region.Width;x++)
            {
                var p=FramePixelReader.Decode(descriptor.Format,pixels.Span.Slice(y*descriptor.Stride+x*descriptor.Format.BytesPerPixel()));
                for(int c=0;c<channels;c++)
                {
                    double v=p.IsGrayscale?p.Gray:c==0?p.R:c==1?p.G:c==2?p.B:p.A;
                    if(!double.IsFinite(v)) continue;
                    count[c]++; min[c]=Math.Min(min[c],v); max[c]=Math.Max(max[c],v); sum[c]+=v;
                }
            }
        }
        return ValueTask.FromResult(new RegionStatistics(descriptor.Format,Enumerable.Range(0,channels).Select(c=>count[c]==0?new ChannelStatistics(0,null,null,null):new(count[c],min[c],max[c],sum[c]/count[c])).ToArray()));
    }
    public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct)
    {
        int stride=checked(region.Width*descriptor.Format.BytesPerPixel());
        var bytes=new byte[checked(stride*region.Height)];
        for(int y=0;y<region.Height;y++)
        {
            ct.ThrowIfCancellationRequested();
            pixels.Span.Slice((region.Y+y)*descriptor.Stride+region.X*descriptor.Format.BytesPerPixel(),stride).CopyTo(bytes.AsSpan(y*stride));
        }
        return ValueTask.FromResult(ImageFrame.TakeOwnership(new(region.Width,region.Height,stride,descriptor.Format),bytes,()=>{}));
    }
}
