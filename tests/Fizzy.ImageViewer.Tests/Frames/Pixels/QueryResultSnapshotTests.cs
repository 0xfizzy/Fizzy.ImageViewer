using Fizzy.ImageViewer.Frames;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class QueryResultSnapshotTests
{
    [Fact]
    public async Task PixelAndStatisticsSnapshotsRejectMutationAndRetainIndependentInputs()
    {
        using var image = ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 });
        using var lease = image.Acquire();
        var queried = await lease.ReadPixelsAsync(new PixelCoordinate[] { new(0, 0) });
        var input = queried.Samples.ToArray();
        var result = new PixelQueryResult(queried.Frame, input);
        input[0] = default;
        Assert.Equal(42, result.Samples[0].Gray);
        Assert.Throws<NotSupportedException>(() => ((IList<PixelSample>)result.Samples)[0] = default);
        var channels = new ChannelStatistics[] { new(1, 42, 42, 42) };
        var statistics = new RegionStatistics(FramePixelFormat.Gray8, channels);
        channels[0] = default;
        Assert.Equal(42, statistics.Channels[0].Mean);
        Assert.Throws<NotSupportedException>(() => ((IList<ChannelStatistics>)statistics.Channels)[0] = default);
    }
}
