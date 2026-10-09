using System.Buffers;
using System.IO;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Snapshots;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class SnapshotOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavePreservesEncodingOrCancellationFailureBeforeFinalReleaseFailure(bool cancel)
    {
        using var stop = new CancellationTokenSource();
        using var memory = new CallbackMemory();
        int releases = 0;
        using var snapshot = new ImageSnapshot(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            memory.Memory, () => { releases++; throw new InvalidOperationException("pixel release"); }),
            default, SnapshotKind.Raw, 0, default);
        memory.OnRead = () =>
        {
            snapshot.Dispose(); // Save must retain the pixels independently until encoding ends.
            if (cancel) stop.Cancel();
            else throw new InvalidOperationException("encoding failure");
        };
        string directory = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "snapshot.tif");
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => snapshot.SaveAsync(path, SnapshotEncoding.Tiff, stop.Token));
            Assert.Equal(2, error.InnerExceptions.Count);
            if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error.InnerExceptions[0]);
            else Assert.Equal("encoding failure", error.InnerExceptions[0].Message);
            Assert.Equal("pixel release", error.InnerExceptions[1].Message);
            Assert.Equal(1, releases);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SavePreservesEncodingFailureWhenTemporaryFileCannotBeDeleted()
    {
        using var memory = new CallbackMemory();
        using var snapshot = new ImageSnapshot(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            memory.Memory, () => { }), default, SnapshotKind.Raw, 0, default);
        string directory = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "snapshot.tif");
        memory.OnRead = () =>
        {
            File.SetAttributes(Assert.Single(Directory.GetFiles(directory)), FileAttributes.ReadOnly);
            throw new InvalidOperationException("encoding failure");
        };
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => snapshot.SaveAsync(path, SnapshotEncoding.Tiff));
            Assert.Equal(2, error.InnerExceptions.Count);
            Assert.Equal("encoding failure", error.InnerExceptions[0].Message);
            Assert.IsType<UnauthorizedAccessException>(error.InnerExceptions[1]);
            Assert.False(File.Exists(path));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FinalReleaseFailureDoesNotUndoSuccessfulFileReplacement()
    {
        using var memory = new CallbackMemory();
        using var snapshot = new ImageSnapshot(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8),
            memory.Memory, () => throw new InvalidOperationException("pixel release")),
            default, SnapshotKind.Raw, 0, default);
        memory.OnRead = snapshot.Dispose;
        string path = Path.Combine(Path.GetTempPath(), $"viewer-{Guid.NewGuid():N}.tif");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => snapshot.SaveAsync(path, SnapshotEncoding.Tiff));
            Assert.Equal("pixel release", error.Message);
            Assert.Equal(new byte[] { 0x49, 0x49, 42, 0 }, (await File.ReadAllBytesAsync(path)).Take(4));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(SnapshotKind.Raw)]
    [InlineData(SnapshotKind.Display)]
    public async Task SourceReleaseFailureCleansUndeliveredSnapshot(SnapshotKind kind)
    {
        var source = new RegionSource();
        var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), 1, source,
            () => throw new InvalidOperationException("source release"));
        var view = new CommittedViewLease(frame.Acquire(), null, 0);
        frame.Dispose();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SnapshotCapture().CaptureAsync(() => view, kind));
        Assert.Equal("source release", error.Message);
        Assert.Equal(1, source.Releases);
    }

    [Fact]
    public async Task RawSnapshotRetainsTransferredRegionUntilFinalLeaseRelease()
    {
        var source = new RegionSource();
        using var frame = ImageFrame.TakeD3D9Surface(new(1, 1, 1, FramePixelFormat.Gray8), 1, source, () => { });
        var snapshot = await new SnapshotCapture().CaptureAsync(() => new(frame.Acquire(), null, 0), SnapshotKind.Raw);
        Assert.Equal(0, source.Releases);
        using var pixels = snapshot.AcquirePixels();
        snapshot.Dispose();
        Assert.Equal(0, source.Releases);
        Assert.Equal(42, pixels.CpuPixels.Span[0]);
        pixels.Dispose();
        Assert.Equal(1, source.Releases);
    }

    private sealed class CallbackMemory : MemoryManager<byte>
    {
        private readonly byte[] _pixels = [42];
        internal Action? OnRead;
        public override Span<byte> GetSpan()
        {
            Interlocked.Exchange(ref OnRead, null)?.Invoke();
            return _pixels;
        }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class RegionSource : IFramePixelSource
    {
        internal int Releases;
        public ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region, CancellationToken ct) =>
            ValueTask.FromResult(ImageFrame.TakeOwnership(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }, () => Releases++));
    }
}
