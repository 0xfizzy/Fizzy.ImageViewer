using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementModelBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentAndCompletionKeepOriginalFailureWhenCleanupAlsoFails(bool completion)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var original = new InvalidOperationException("original operation");
            var cleanup = new InvalidOperationException("cleanup");
            var collection = viewer.Host.Measurements;
            var overlay = viewer.Host.Window.MeasurementOverlay;
            collection.ItemRemoving += _ => throw cleanup;
            if (completion) collection.ItemCompleted += _ => throw original;
            else overlay.VisualAdded += _ => throw original;
            MeasurementItem? item = null;
            var error = Assert.Throws<AggregateException>(() =>
            {
                item = (MeasurementItem)new MeasurementCreationContext(collection, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame)
                    .CreateMeasurement(MeasurementGeometry.Point(new()));
                item.Complete();
            });
            Assert.Same(original, error.InnerExceptions[0]);
            Assert.Contains(cleanup, error.Flatten().InnerExceptions);
            Assert.Empty(overlay.Canvas.Children);
            if (item != null) { Assert.True(item.IsDisposed); item.Dispose(); }
        });
    }

    [Fact]
    public void TypedResultsOwnTheirPayloadsAndDoNotExposeUnrelatedFields()
    {
        var frame = new FrameInfo(1, new(1, 1, 1, FramePixelFormat.Gray8), null);
        PixelCoordinate[] coordinates = [new(0, 0)];
        PixelSample[] samples = [new(FramePixelFormat.Gray8, 7, 0, 0, 0, 255)];
        var pixels = new MeasurementSampleResult(Guid.NewGuid(), 0, frame, MeasurementQueryKind.Pixel, coordinates, samples);
        coordinates[0] = new(9, 9);
        samples[0] = samples[0] with { Gray = 99 };
        Assert.Equal(new PixelCoordinate(0, 0), pixels.Coordinates[0]);
        Assert.Equal(7, pixels.Samples[0].Gray);
        ChannelStatistics[] channels = [new(1, 7, 7, 7)];
        var region = new MeasurementRegionResult(Guid.NewGuid(), 0, frame, new(0, 0, 1, 1), channels);
        channels[0] = default;
        Assert.Equal(7, region.Channels[0].Mean);
        Assert.Null(typeof(MeasurementSampleResult).GetProperty("Region"));
        Assert.Null(typeof(MeasurementRegionResult).GetProperty("Samples"));
        Assert.Throws<NotSupportedException>(() => ((IList<ChannelStatistics>)region.Channels)[0] = default);
    }
}
