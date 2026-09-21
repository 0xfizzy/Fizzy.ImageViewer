using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BitMiracle.LibTiff.Classic;
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
        var d = frame.Descriptor;
        bool gray = d.Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float;
        bool alpha = d.Format is FramePixelFormat.Bgra32 or FramePixelFormat.Pbgra32;
        int channels = gray ? 1 : alpha ? 4 : 3;
        int bits = d.Format == FramePixelFormat.Gray16 ? 16 : d.Format == FramePixelFormat.Gray32Float ? 32 : 8;
        int rowBytes = checked(d.Width * channels * (bits / 8));
        if ((long)rowBytes * d.Height + (long)d.Height * 16 + 65536 >= uint.MaxValue)
            throw new NotSupportedException("Image exceeds Classic TIFF size limit.");
        using var tiff = Tiff.Open(path, "wl") ?? throw new IOException("Cannot create TIFF.");
        tiff.SetField(TiffTag.IMAGEWIDTH, d.Width);
        tiff.SetField(TiffTag.IMAGELENGTH, d.Height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, channels);
        tiff.SetField(TiffTag.BITSPERSAMPLE, bits);
        tiff.SetField(TiffTag.SAMPLEFORMAT, d.Format == FramePixelFormat.Gray32Float ? SampleFormat.IEEEFP : SampleFormat.UINT);
        tiff.SetField(TiffTag.PHOTOMETRIC, gray ? Photometric.MINISBLACK : Photometric.RGB);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.COMPRESSION, Compression.NONE);
        tiff.SetField(TiffTag.ROWSPERSTRIP, Math.Max(1, 65536 / rowBytes));
        if (alpha) tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)(d.Format == FramePixelFormat.Pbgra32 ? ExtraSample.ASSOCALPHA : ExtraSample.UNASSALPHA) });
        byte[] row = new byte[rowBytes];
        for (int y = 0; y < d.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var src = frame.CpuPixels.Span.Slice(y * d.Stride, d.RowBytes);
            if (gray || d.Format == FramePixelFormat.Rgb24) src.CopyTo(row);
            else
            {
                int bpp = d.Format.BytesPerPixel();
                for (int x = 0; x < d.Width; x++)
                {
                    row[x * channels] = src[x * bpp + 2];
                    row[x * channels + 1] = src[x * bpp + 1];
                    row[x * channels + 2] = src[x * bpp];
                    if (alpha) row[x * channels + 3] = src[x * bpp + 3];
                }
            }
            if (!tiff.WriteScanline(row, y)) throw new IOException("TIFF scanline write failed.");
        }
        if (!tiff.WriteDirectory()) throw new IOException("TIFF directory write failed.");
    }
}
