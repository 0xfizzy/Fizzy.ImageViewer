using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Snapshots;

internal sealed class SnapshotCapture
{
    // Acquired work can outlive Viewer shutdown. Do not dispose this semaphore at shutdown.
    private readonly SemaphoreSlim _exportGate = new(1, 1);

    /// <summary>Consumes view, including on cancellation or failure.</summary>
    internal Task<ImageSnapshot> CaptureAsync(CommittedViewLease view, SnapshotKind kind,
        PixelRegion? region = null, CancellationToken ct = default) => Task.Run(async () =>
    {
        RegionPixels? read = null;
        ImageSnapshot? snapshot = null;
        bool entered = false;
        try
        {
            if (kind is not SnapshotKind.Raw and not SnapshotKind.Display)
                throw new ArgumentOutOfRangeException(nameof(kind));
            await _exportGate.WaitAsync(ct).ConfigureAwait(false);
            entered = true;
            ct.ThrowIfCancellationRequested();
            var crop = region ?? PixelRegion.Full(view.Frame.Descriptor);
            read = await view.Frame.ReadRegionAsync(crop, ct).ConfigureAwait(false);
            if (kind == SnapshotKind.Raw)
                snapshot = new ImageSnapshot(read.TransferPixels(), view.Frame.Info, kind, view.Version, crop);
            else
            {
                using var cpu = read.AcquirePixels();
                using var pixels = DisplayConverter.Convert(cpu, view.Range, ct, normalize: true);
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
        finally { if (entered) _exportGate.Release(); }
    });
}
