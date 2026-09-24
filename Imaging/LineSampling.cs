using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>Clips a source-image line and returns ordered integer sample coordinates.</summary>
internal static class LineSampling
{
    public static PixelCoordinate[] GetCoordinates(FrameDescriptor descriptor, double x0, double y0, double x1, double y1)
    {
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)) return [];
        double dx = x1 - x0, dy = y1 - y0;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return [];
        double lo = 0, hi = 1;
        if (!Clip(-dx, x0, ref lo, ref hi) || !Clip(dx, descriptor.Width - 1 - x0, ref lo, ref hi) ||
            !Clip(-dy, y0, ref lo, ref hi) || !Clip(dy, descriptor.Height - 1 - y0, ref lo, ref hi)) return [];
        int ax = Math.Clamp((int)Math.Round(x0 + lo * dx), 0, descriptor.Width - 1);
        int ay = Math.Clamp((int)Math.Round(y0 + lo * dy), 0, descriptor.Height - 1);
        int bx = Math.Clamp((int)Math.Round(x0 + hi * dx), 0, descriptor.Width - 1);
        int by = Math.Clamp((int)Math.Round(y0 + hi * dy), 0, descriptor.Height - 1);
        int nx = Math.Abs(bx - ax), ny = Math.Abs(by - ay), sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1;
        int count = Math.Max(nx, ny) + 1;
        var coordinates = new PixelCoordinate[count];
        long err = (long)nx - ny;
        for (int i = 0; i < count; i++)
        {
            coordinates[i] = new(ax, ay);

            if (ax == bx && ay == by) break;
            long e = 2 * err;
            if (e > -ny) { err -= ny; ax += sx; }
            if (e < nx) { err += nx; ay += sy; }
        }
        return coordinates;
    }
    private static bool Clip(double p, double q, ref double lo, ref double hi)
    {
        if (p == 0) return q >= 0;
        double t = q / p;
        if (p < 0) { if (t > hi) return false; lo = Math.Max(lo, t); }
        else { if (t < lo) return false; hi = Math.Min(hi, t); }
        return true;
    }
}
