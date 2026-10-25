using System.Buffers;
using System.Windows;
using ApplicationEndpoint;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApplicationEndpoint.Tests;

public sealed class EndpointTests
{
    [Fact]
    public void ApplicationInterfacesResolveToOneEndpoint()
    {
        using var services = App.BuildServices();
        var endpoint = services.GetRequiredService<ImageViewerEndpoint>();
        Assert.Same(endpoint, services.GetRequiredService<IImageSink>());
        Assert.Same(endpoint, services.GetRequiredService<IImageDisplay>());
        Assert.Same(endpoint, services.GetRequiredService<IImageNotifications>());
    }

    [Fact]
    public async Task UnboundAndStoppedConsumeInputAndRejectCommands()
    {
        var endpoint = new ImageViewerEndpoint();
        var unbound = new CountingOwner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => endpoint.PublishAsync(Frame(unbound), default));
        Assert.Equal(1, unbound.Releases);
        Assert.Throws<InvalidOperationException>(endpoint.FitImage);
        endpoint.StopAccepting();
        var stopped = new CountingOwner();
        await Assert.ThrowsAsync<DisplaySessionStoppedException>(() => endpoint.PublishAsync(Frame(stopped), default));
        Assert.Equal(1, stopped.Releases);
        Assert.Throws<DisplaySessionStoppedException>(endpoint.StartLengthMeasurement);
    }

    [Fact]
    public Task CancellationValidationAndMemoryFailureReleaseOnce() => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl();
        var endpoint = new ImageViewerEndpoint();
        endpoint.Attach(control);
        try
        {
            var cancelled = new CountingOwner();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => endpoint.PublishAsync(Frame(cancelled), new(true)));
            Assert.Equal(1, cancelled.Releases);
            var invalid = new CountingOwner();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => endpoint.PublishAsync(new GrayFrame(0, 2, 2, invalid), default));
            Assert.Equal(1, invalid.Releases);
            var broken = new CountingOwner { FailRead = true, FailRelease = true };
            var error = await Assert.ThrowsAsync<AggregateException>(() => endpoint.PublishAsync(Frame(broken), default));
            Assert.Equal(2, error.InnerExceptions.Count);
            Assert.Equal(1, broken.Releases);
        }
        finally { endpoint.StopAccepting(); endpoint.Detach(); }
    });

    [Fact]
    public Task AcceptedBufferLivesUntilControlDisposalAndBindingIsTerminal() => ControlTestDispatcher.Run(async () =>
    {
        var control = new ImageViewerControl();
        var endpoint = new ImageViewerEndpoint();
        var owner = new CountingOwner();
        try
        {
            endpoint.Attach(control);
            Assert.Throws<InvalidOperationException>(() => endpoint.Attach(control));
            await Task.Run(() => endpoint.PublishAsync(Frame(owner), default));
            Assert.Equal(0, owner.Releases); // Hidden control retains the accepted input without copying it.
            endpoint.StopAccepting();
            endpoint.Detach();
            Assert.Equal(0, owner.Releases); // Endpoint borrows; it must not dispose the control.
            Assert.Throws<InvalidOperationException>(() => endpoint.Attach(control));
        }
        finally { await control.DisposeAsync(); }
        Assert.Equal(1, owner.Releases);
        await control.DisposeAsync();
        Assert.Equal(1, owner.Releases);
    });

    [Fact]
    public Task HostCleanupStopsProducerAndSharesCompletion() => ControlTestDispatcher.Run(async () =>
    {
        using var services = App.BuildServices();
        var window = new MainWindow();
        window.Initialize(services, () => { });
        window.Show();
        try
        {
            await Task.Delay(100);
            var cleanup = window.CleanupAsync();
            Assert.Same(cleanup, window.CleanupAsync());
            Assert.Empty(await cleanup);
            Assert.False(services.GetRequiredService<DisplayViewModel>().FitCommand.CanExecute(null));
        }
        finally { await window.CleanupAsync(); window.Close(); }
    });

    private static GrayFrame Frame(CountingOwner owner) => new(2, 2, 2, owner);

    private sealed class CountingOwner : IMemoryOwner<byte>
    {
        public int Releases;
        public bool FailRead, FailRelease;
        public Memory<byte> Memory => FailRead ? throw new InvalidOperationException("memory failed") : new byte[4];
        public void Dispose()
        {
            Interlocked.Increment(ref Releases);
            if (FailRelease) throw new InvalidOperationException("release failed");
        }
    }
}
