using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

internal static class DisplayConverter
{
    public static DisplayBuffer Convert(FrameLease frame, GrayDisplayRange? range, CancellationToken ct, bool normalize = false)
    {
        var d = frame.Descriptor;
        bool native = !normalize && d.Format is not (FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float) &&
            (d.Format != FramePixelFormat.Gray8 || range == null || range.Value == new GrayDisplayRange(0, 255));
        var output = new DisplayBuffer(d.Width, d.Height, native ? d.Format : FramePixelFormat.Pbgra32);
        try
        {
            if (native)
            {
                for (int y = 0; y < d.Height; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    frame.CpuPixels.Span.Slice(y * d.Stride, d.RowBytes).CopyTo(output.Bytes.AsSpan(y * output.Stride));
                }
                return output;
            }
            var bounds = range ?? new GrayDisplayRange(0, d.Format == FramePixelFormat.Gray16 ? 65535 : d.Format == FramePixelFormat.Gray32Float ? 1 : 255);
            Span<byte> gray8 = stackalloc byte[256];
            if (d.Format == FramePixelFormat.Gray8)
                for (int i = 0; i < gray8.Length; i++) gray8[i] = MapGray(i, bounds);
            int bpp = d.Format.BytesPerPixel();
            for (int y = 0; y < d.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                var row = frame.CpuPixels.Span.Slice(y * d.Stride, d.RowBytes);
                var dst = output.Bytes.AsSpan(y * output.Stride, output.Stride);
                if (d.Format == FramePixelFormat.Pbgra32) { row.CopyTo(dst); continue; }
                for (int x = 0; x < d.Width; x++)
                {
                    int i = x * 4;
                    int s = x * bpp;
                    if (d.Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float)
                    {
                        byte gray = d.Format == FramePixelFormat.Gray8 ? gray8[row[s]] :
                            MapGray(FramePixelReader.Decode(d.Format, row.Slice(s)).Gray, bounds);
                        dst[i] = dst[i + 1] = dst[i + 2] = gray;
                        dst[i + 3] = 255;
                    }
                    else
                    {
                        if (d.Format == FramePixelFormat.Bgra32)
                        {
                            int alpha = row[s + 3];
                            dst[i] = (byte)((row[s] * alpha + 127) / 255);
                            dst[i + 1] = (byte)((row[s + 1] * alpha + 127) / 255);
                            dst[i + 2] = (byte)((row[s + 2] * alpha + 127) / 255);
                            dst[i + 3] = (byte)alpha;
                        }
                        else
                        {
                            bool rgb = d.Format == FramePixelFormat.Rgb24;
                            dst[i] = row[s + (rgb ? 2 : 0)];
                            dst[i + 1] = row[s + 1];
                            dst[i + 2] = row[s + (rgb ? 0 : 2)];
                            dst[i + 3] = 255;
                        }
                    }
                }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
    private static byte MapGray(double value, GrayDisplayRange bounds) =>
        double.IsNaN(value) || value <= bounds.Minimum ? (byte)0 : value >= bounds.Maximum ? (byte)255 :
        (byte)Math.Round((value - bounds.Minimum) / (bounds.Maximum - bounds.Minimum) * 255);
}
