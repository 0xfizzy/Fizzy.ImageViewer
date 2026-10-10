using Fizzy.ImageViewer.Cuda;
using Fizzy.ImageViewer.Frames;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class CudaBridgeTests
{
    [Fact]
    public async Task CancellationAndUnsupportedInputTransferOwnershipWithoutNativeLoading()
    {
        var backend = new Backend();
        var cancelled = new Source();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CudaImageFrame.CreateAsync(cancelled, backend, new(true)).AsTask());
        Assert.Equal(1, cancelled.Disposals);
        Assert.Equal(0, cancelled.Executions);
        var unsupported = new Source { Descriptor = new(1, 1, 2, FramePixelFormat.Gray16) };
        await Assert.ThrowsAsync<NotSupportedException>(() => CudaImageFrame.CreateAsync(unsupported, backend, default).AsTask());
        Assert.Equal(1, unsupported.Disposals);
        Assert.Equal(0, backend.Created);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("write")]
    [InlineData("wait")]
    [InlineData("complete")]
    public async Task FailuresKeepResourceOwnershipAtTheCorrectBoundary(string phase)
    {
        var source = new Source { Failure = phase };
        var backend = new Backend { Failure = phase };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CudaImageFrame.CreateAsync(source, backend, default).AsTask());
        Assert.Equal(1, source.Disposals);
        Assert.Equal(phase == "register" ? 1 : 0, backend.Released);
        // A failed submission is not proof of safe resource release. The fake bridge
        // explicitly establishes that proof later, independent of public failure.
        source.Scope!.CompleteAccess();
        Assert.Equal(1, backend.Released);
    }

    [Fact]
    public async Task IndependentLeaseKeepsSourceAndSurfaceAliveForQueries()
    {
        var source = new Source(); var backend = new Backend();
        using var frame = await CudaImageFrame.CreateAsync(source, backend, default);
        using var lease = frame.Acquire();
        frame.Dispose();
        Assert.Equal(0, source.Disposals);
        var samples = await Task.WhenAll(lease.ReadPixelsAsync(new PixelCoordinate[] { new(0, 0) }).AsTask(),
            lease.ReadPixelsAsync(new PixelCoordinate[] { new(0, 0) }).AsTask());
        Assert.All(samples, result => Assert.Equal(42, result.Samples[0].Gray));
        lease.Dispose();
        Assert.Equal(1, source.Disposals);
        Assert.Equal(1, backend.Released);
    }

    [Fact]
    public async Task CancellationAfterWriteWaitsForCompletionBeforeReleasingResources()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source();
        var backend = new Backend { AfterWrite = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CudaImageFrame.CreateAsync(source, backend, cancellation.Token).AsTask());
        Assert.True(source.Scope!.Waited);
        Assert.Equal(1, backend.Released);
        Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public async Task OperationAndSourceCleanupErrorsArePreserved()
    {
        var source = new Source { Failure = "complete", FailDispose = true };
        var error = await Assert.ThrowsAsync<AggregateException>(() => CudaImageFrame.CreateAsync(source, new Backend(), default).AsTask());
        Assert.Equal("complete", error.InnerExceptions[0].Message);
        Assert.Equal("source release", error.InnerExceptions[1].Message);
        source.Scope!.CompleteAccess();
    }

    private sealed class Source : ICudaFrameSource
    {
        public FrameDescriptor Descriptor { get; init; } = new(1, 1, 1, FramePixelFormat.Gray8);
        internal string? Failure;
        internal bool FailDispose;
        internal int Disposals, Executions;
        internal Scope? Scope;
        public ValueTask ExecuteAsync(Action<ICudaExecutionScope> action, CancellationToken ct)
        {
            Executions++;
            var scope = new Scope(Failure); Scope = scope;
            action(scope);
            if (Failure == "complete") throw new InvalidOperationException("complete");
            scope.CompleteAccess();
            return ValueTask.CompletedTask;
        }
        public void Dispose() { Disposals++; if (FailDispose) throw new InvalidOperationException("source release"); }
    }
    private sealed class Scope(string? failure) : ICudaExecutionScope
    {
        public nint Context => 1;
        public nint Stream => 2;
        public nint InputPointer => 3;
        public long RowStride => 1;
        public long InputByteLength => 1;
        internal Token? Retained;
        internal bool Waited;
        public nint AllocateScratch(long bytes, CudaMemoryKind kind) => throw new NotSupportedException();
        public IDisposable RetainResource(ICudaResource resource)
        {
            if (failure == "register") throw new InvalidOperationException("register");
            return Retained = new Token(resource);
        }
        public void WaitForCompletion() { if (failure == "wait") throw new InvalidOperationException("wait"); Waited = true; }
        internal void CompleteAccess() => Retained?.Complete();
        internal sealed class Token(ICudaResource resource) : IDisposable
        {
            private int _references = 2;
            private bool _disposed;
            public void Dispose() { if (!_disposed) { _disposed = true; Release(); } }
            internal void Complete() => Release();
            private void Release() { if (--_references == 0) resource.Release(); }
        }
    }
    private sealed class Backend : ICudaBackend
    {
        internal int Created, Released;
        internal string? Failure;
        internal Action? AfterWrite;
        public CudaSurface CreateSurface(nint context, int width, int height) { Created++; return new(5, 6, _ => Released++); }
        public void WriteSurface(CudaSurface surface, ICudaExecutionScope scope, FrameDescriptor descriptor, int format)
        {
            Assert.NotNull(((Scope)scope).Retained);
            if (Failure == "write") throw new InvalidOperationException("write");
            AfterWrite?.Invoke();
        }
        public byte[] Query(ICudaExecutionScope scope, FrameDescriptor descriptor, int kind, int[]? points, PixelRegion region) => [42];
    }
}
