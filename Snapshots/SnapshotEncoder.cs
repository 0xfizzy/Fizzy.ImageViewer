using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Snapshots;

internal static class SnapshotEncoder
{
    public static void Save(FrameLease frame, SnapshotKind kind, string path, SnapshotEncoding encoding, CancellationToken ct)
    {
        if (kind == SnapshotKind.Raw && encoding != SnapshotEncoding.Tiff)
            throw new ArgumentException("Raw snapshots require TIFF.");
        path = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            ct.ThrowIfCancellationRequested();
            if (encoding == SnapshotEncoding.Tiff) WriteTiff(frame, temp, ct);
            else WriteDisplay(frame, temp, encoding, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteDisplay(FrameLease frame, string path, SnapshotEncoding encoding, CancellationToken ct)
    {
        var d = frame.Descriptor;
        byte[] pixels = frame.CpuPixels.ToArray();
        PixelFormat format = PixelFormats.Pbgra32;
        if (encoding == SnapshotEncoding.Jpeg)
        {
            // RGB is already premultiplied, so dropping alpha composites onto black.
            for (int y = 0; y < d.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (int x = 0; x < d.Width; x++) pixels[y * d.Stride + x * 4 + 3] = 255;
            }
            format = PixelFormats.Bgr32;
        }
        var bitmap = BitmapSource.Create(d.Width, d.Height, 96, 96, format, null, pixels, d.Stride);
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
    private static void WriteTiff(FrameLease frame, string path, CancellationToken ct)
    {
        using var stream = File.Create(path);
        TiffSnapshotWriter.Write(frame, stream, ct);
    }
}
