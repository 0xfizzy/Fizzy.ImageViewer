using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Snapshots;

internal sealed class SnapshotCapture
{
    // Acquired work can outlive Viewer shutdown. Do not dispose this semaphore at shutdown.
    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private readonly SemaphoreSlim _capacity = new(2, 2);

    /// <summary>Reserves one of two export slots before synchronously acquiring an owned view.
    /// The acquired view is consumed on every outcome; rejected requests never acquire it.</summary>
    internal Task<ImageSnapshot> CaptureAsync(Func<CommittedViewLease> acquireView, SnapshotKind kind,
        PixelRegion? region = null, CancellationToken cancellationToken = default)
    {
        if (!_capacity.Wait(0))
            throw new InvalidOperationException("Snapshot capture capacity is full (one executing and one waiting).");
        CommittedViewLease view;
        try { view = acquireView(); }
        catch { _capacity.Release(); throw; }
        return CaptureOwnedAsync(view, kind, region, cancellationToken);
    }

    private Task<ImageSnapshot> CaptureOwnedAsync(CommittedViewLease view, SnapshotKind kind,
        PixelRegion? region, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        RegionPixels? read = null;
        ImageSnapshot? snapshot = null;
        bool entered = false;
        try
        {
            if (kind is not SnapshotKind.Raw and not SnapshotKind.Display)
                throw new ArgumentOutOfRangeException(nameof(kind));
            await _exportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();
            var crop = region ?? PixelRegion.Full(view.Frame.Descriptor);
            read = await view.Frame.ReadRegionAsync(crop, cancellationToken).ConfigureAwait(false);
            if (kind == SnapshotKind.Raw)
                snapshot = new ImageSnapshot(read.TransferPixels(), view.Frame.Info, kind, view.Version, crop);
            else
            {
                using var cpu = read.AcquirePixels();
                using var pixels = DisplayConverter.Convert(cpu, view.Range, cancellationToken, normalize: true);
                snapshot = new ImageSnapshot(ImageFrame.Copy(new(pixels.Width, pixels.Height, pixels.Stride, FramePixelFormat.Pbgra32),
                    pixels.Bytes.AsSpan(0, pixels.Length)), view.Frame.Info, kind, view.Version, crop);
            }
            read.Dispose();
            view.Dispose();
            return snapshot;
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            try { snapshot?.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { read?.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { view.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
            if (errors.Count > 1) throw new AggregateException(errors);
            throw;
        }
        finally
        {
            if (entered) _exportGate.Release();
            _capacity.Release();
        }
    });
}
