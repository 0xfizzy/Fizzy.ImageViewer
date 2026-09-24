using Fizzy.ImageViewer.Frames;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class PixelQueryTests
{
    [Fact]
    public async Task QueriesRetainGpuOwnerWithoutImplicitDownload()
    {
        int releases=0;
        var source=new DelayedSource();
        var frame=ImageFrame.TakeD3D9Surface(new(4,3,4,FramePixelFormat.Gray8),(nint)1,source,()=>releases++);
        var lease=frame.Acquire();frame.Dispose();Assert.False(lease.TryGetCpuPixels(out _));
        var pending=lease.ReadPixelsAsync(new[]{new PixelCoordinate(2,1)}).AsTask();
        lease.Dispose();Assert.Equal(0,releases);source.Ready.SetResult();
        Assert.Equal(42,(await pending).Samples[0].Gray);Assert.Equal(1,releases);Assert.Equal(0,source.RegionCalls);
    }
    [Fact]
    public async Task RegionStatisticsAndCropPreservePaddedAndFloatPixels()
    {
        using var frame=ImageFrame.Copy(new(2,2,4,FramePixelFormat.Gray8),new byte[]{1,2,99,99,3,4});
        using var lease=frame.Acquire();
        var stats=await lease.ComputeRegionStatisticsAsync(new(1,0,1,2));
        Assert.Equal(new ChannelStatistics(2,2,4,3),stats.Statistics.Channels[0]);
        using var region=await lease.ReadRegionAsync(new(1,0,1,2));using var pixels=region.AcquirePixels();
        Assert.Equal(new byte[]{2,4},pixels.CpuPixels.ToArray());
        using var floats=ImageFrame.Copy(new(3,1,12,FramePixelFormat.Gray32Float),new[]{float.NaN,float.PositiveInfinity,5f}.SelectMany(BitConverter.GetBytes).ToArray());
        using var f=floats.Acquire();Assert.Equal(new ChannelStatistics(1,5,5,5),(await f.ComputeRegionStatisticsAsync(new(0,0,3,1))).Statistics.Channels[0]);
    }
    [Fact]
    public void RegionsClipToFrameBounds()
    {
        Assert.Equal(new PixelRegion(0, 1, 3, 3), PixelRegion.Clip(-.5, 1.2, 3.1, 2.1, new(10, 10, 10, FramePixelFormat.Gray8)));
    }
    [Fact]
    public async Task CancelledRegionRetainsSourceUntilCompletionAndDisposesOutput()
    {
        int sourceReleased=0,outputReleased=0;
        var source=new DelayedRegion(()=>outputReleased++);
        var frame=ImageFrame.TakeD3D9Surface(new(2,2,2,FramePixelFormat.Gray8),(nint)1,source,()=>sourceReleased++);
        var lease=frame.Acquire();frame.Dispose();
        using var stop=new CancellationTokenSource();
        var pending=lease.ReadRegionAsync(new(1,1,1,1),stop.Token).AsTask();
        lease.Dispose();stop.Cancel();
        Assert.Equal(0,sourceReleased);
        source.Ready.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);
        Assert.Equal(1,sourceReleased);Assert.Equal(1,outputReleased);
    }
    private sealed class DelayedRegion(Action release) : IFramePixelSource
    {
        public TaskCompletionSource Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region,CancellationToken ct)=>throw new NotSupportedException();
        public async ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region,CancellationToken ct)
        {
            await Ready.Task;
            return ImageFrame.TakeOwnership(new(1,1,1,FramePixelFormat.Gray8),new byte[]{7},release);
        }
    }
    private sealed class DelayedSource : IFramePixelSource
    {
        public TaskCompletionSource Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RegionCalls;
        public async ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> points,CancellationToken ct) { await Ready.Task;return [new(FramePixelFormat.Gray8,42,0,0,0,255)]; }
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region,CancellationToken ct) { RegionCalls++;throw new NotSupportedException(); }
    }
}
