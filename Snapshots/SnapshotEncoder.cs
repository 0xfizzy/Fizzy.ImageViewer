using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

internal static class SnapshotEncoder
{
    public static void Save(FrameLease frame, SnapshotKind kind, string path, SnapshotEncoding encoding, CancellationToken cancellationToken)
    {
        if (kind == SnapshotKind.Raw && encoding != SnapshotEncoding.Tiff)
            throw new ArgumentException("Raw snapshots require TIFF.");
        path = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encoding == SnapshotEncoding.Tiff) WriteTiff(frame, temp, cancellationToken);
            else WriteDisplay(frame, temp, encoding, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        catch (Exception error)
        {
            try { File.Delete(temp); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }
    private static void WriteDisplay(FrameLease frame, string path, SnapshotEncoding encoding, CancellationToken cancellationToken)
    {
        var descriptor = frame.Descriptor;
        byte[] pixels = frame.CpuPixels.ToArray();
        PixelFormat format = PixelFormats.Pbgra32;
        if (encoding == SnapshotEncoding.Jpeg)
        {
            // RGB is already premultiplied, so dropping alpha composites onto black.
            for (int y = 0; y < descriptor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < descriptor.Width; x++) pixels[y * descriptor.Stride + x * 4 + 3] = 255;
            }
            format = PixelFormats.Bgr32;
        }
        var bitmap = BitmapSource.Create(descriptor.Width, descriptor.Height, 96, 96, format, null, pixels, descriptor.Stride);
        bitmap.Freeze();
        BitmapEncoder encoder = encoding switch
        {
            SnapshotEncoding.Png => new PngBitmapEncoder(),
            SnapshotEncoding.Jpeg => new JpegBitmapEncoder(),
            SnapshotEncoding.Bmp => new BmpBitmapEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static void WriteTiff(FrameLease frame, string path, CancellationToken cancellationToken)
    {
        using var stream = File.Create(path);
        TiffSnapshotWriter.Write(frame, stream, cancellationToken);
    }
}
