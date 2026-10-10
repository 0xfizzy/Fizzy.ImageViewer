using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

internal static class DisplayConverter
{
    public static DisplayBuffer Convert(FrameLease frame, GrayDisplayRange? range, CancellationToken cancellationToken, bool normalize = false)
    {
        var descriptor = frame.Descriptor;
        var info = descriptor.Format.GetInfo();
        bool native = !normalize && descriptor.Format is not (FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float or FramePixelFormat.Rgba32) &&
            (descriptor.Format != FramePixelFormat.Gray8 || range == null || range.Value == new GrayDisplayRange(0, 255));
        var output = new DisplayBuffer(descriptor.Width, descriptor.Height, native ? descriptor.Format : FramePixelFormat.Pbgra32);
        try
        {
            if (native)
            {
                for (int y = 0; y < descriptor.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    frame.CpuPixels.Span.Slice(y * descriptor.Stride, descriptor.RowBytes).CopyTo(output.Bytes.AsSpan(y * output.Stride));
                }
                return output;
            }
            var bounds = range ?? new GrayDisplayRange(0, descriptor.Format == FramePixelFormat.Gray16 ? 65535 : descriptor.Format == FramePixelFormat.Gray32Float ? 1 : 255);
            Span<byte> gray8 = stackalloc byte[256];
            if (descriptor.Format == FramePixelFormat.Gray8)
                for (int i = 0; i < gray8.Length; i++) gray8[i] = MapGray(i, bounds);
            int bpp = info.BytesPerPixel;
            for (int y = 0; y < descriptor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = frame.CpuPixels.Span.Slice(y * descriptor.Stride, descriptor.RowBytes);
                var dst = output.Bytes.AsSpan(y * output.Stride, output.Stride);
                if (descriptor.Format == FramePixelFormat.Pbgra32) { row.CopyTo(dst); continue; }
                for (int x = 0; x < descriptor.Width; x++)
                {
                    int i = x * 4;
                    int s = x * bpp;
                    if (info.IsGrayscale)
                    {
                        byte gray = descriptor.Format == FramePixelFormat.Gray8 ? gray8[row[s]] :
                            MapGray(CpuFramePixelReader.Decode(descriptor.Format, row.Slice(s)).Gray, bounds);
                        dst[i] = dst[i + 1] = dst[i + 2] = gray;
                        dst[i + 3] = 255;
                    }
                    else
                    {
                        if (descriptor.Format is FramePixelFormat.Bgra32 or FramePixelFormat.Rgba32)
                        {
                            int alpha = row[s + 3];
                            bool rgba = descriptor.Format == FramePixelFormat.Rgba32;
                            dst[i] = (byte)((row[s + (rgba ? 2 : 0)] * alpha + 127) / 255);
                            dst[i + 1] = (byte)((row[s + 1] * alpha + 127) / 255);
                            dst[i + 2] = (byte)((row[s + (rgba ? 0 : 2)] * alpha + 127) / 255);
                            dst[i + 3] = (byte)alpha;
                        }
                        else
                        {
                            bool rgb = descriptor.Format == FramePixelFormat.Rgb24;
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
