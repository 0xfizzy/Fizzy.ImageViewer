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
        using (view)
        {
            if (kind is not SnapshotKind.Raw and not SnapshotKind.Display)
                throw new ArgumentOutOfRangeException(nameof(kind));
            await _exportGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var crop = region ?? PixelRegion.Full(view.Frame.Descriptor);
                using var read = await view.Frame.ReadRegionAsync(crop, ct).ConfigureAwait(false);
                using var cpu = read.AcquirePixels();
                if (kind == SnapshotKind.Raw)
                    return new ImageSnapshot(ImageFrame.Copy(cpu.Descriptor, cpu.CpuPixels.Span), view.Frame.Info, kind, view.Version, crop);
                using var pixels = DisplayConverter.Convert(cpu, view.Range, ct, normalize: true);
                return new ImageSnapshot(ImageFrame.Copy(new(pixels.Width, pixels.Height, pixels.Stride, FramePixelFormat.Pbgra32),
                    pixels.Bytes.AsSpan(0, pixels.Length)), view.Frame.Info, kind, view.Version, crop);
            }
            finally { _exportGate.Release(); }
        }
    });
}
