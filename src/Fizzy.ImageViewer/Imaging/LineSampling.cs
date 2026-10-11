using Fizzy.ImageViewer.Frames;
using System.Numerics;

namespace Fizzy.ImageViewer.Imaging;

/// <summary>Clips to the half-open pixel coverage rectangle, floors endpoints to pixel indices,
/// and rasterizes in the original endpoint order, including both endpoint pixels.</summary>
internal static class LineSampling
{
    public static PixelCoordinate[] GetCoordinates(FrameDescriptor descriptor, double x0, double y0, double x1, double y1)
    {
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)) return [];
        // Beyond the full 32-bit pixel-coordinate range, cancellation can erase the
        // clipped interval (both parameters round to the same double). Keep ordinary
        // image coordinates on the floating path and preserve exact binary inputs here.
        if (Math.Max(Math.Max(Math.Abs(x0), Math.Abs(x1)), Math.Max(Math.Abs(y0), Math.Abs(y1))) > uint.MaxValue)
            return GetExtremeCoordinates(descriptor, x0, y0, x1, y1);
        double dx = x1 - x0, dy = y1 - y0;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return [];
        // A line on the exclusive right/bottom edge has no pixels. Crossing endpoints
        // may touch that edge and are clamped to the last covered pixel after clipping.
        if (Math.Min(x0, x1) >= descriptor.Width || Math.Min(y0, y1) >= descriptor.Height) return [];
        double lo = 0, hi = 1;
        if (!Clip(-dx, x0, ref lo, ref hi) || !Clip(dx, descriptor.Width - x0, ref lo, ref hi) ||
            !Clip(-dy, y0, ref lo, ref hi) || !Clip(dy, descriptor.Height - y0, ref lo, ref hi)) return [];
        if (lo == hi && (x0 + lo * dx >= descriptor.Width || y0 + lo * dy >= descriptor.Height)) return [];
        int ax = Math.Clamp((int)Math.Floor(x0 + lo * dx), 0, descriptor.Width - 1);
        int ay = Math.Clamp((int)Math.Floor(y0 + lo * dy), 0, descriptor.Height - 1);
        int bx = Math.Clamp((int)Math.Floor(x0 + hi * dx), 0, descriptor.Width - 1);
        int by = Math.Clamp((int)Math.Floor(y0 + hi * dy), 0, descriptor.Height - 1);
        return Rasterize(ax, ay, bx, by);
    }

    private static PixelCoordinate[] Rasterize(int ax, int ay, int bx, int by)
    {
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

    private static PixelCoordinate[] GetExtremeCoordinates(FrameDescriptor descriptor, double x0, double y0, double x1, double y1)
    {
        int shift = -Math.Min(0, Math.Min(Math.Min(Exponent(x0), Exponent(y0)), Math.Min(Exponent(x1), Exponent(y1))));
        BigInteger ax = Scale(x0, shift), ay = Scale(y0, shift), bx = Scale(x1, shift), by = Scale(y1, shift);
        BigInteger width = (BigInteger)descriptor.Width << shift, height = (BigInteger)descriptor.Height << shift;
        if (BigInteger.Min(ax, bx) >= width || BigInteger.Min(ay, by) >= height) return [];
        BigInteger dx = bx - ax, dy = by - ay;
        var lo = new Fraction(0, 1);
        var hi = new Fraction(1, 1);
        if (!ClipExact(-dx, ax, ref lo, ref hi) || !ClipExact(dx, width - ax, ref lo, ref hi) ||
            !ClipExact(-dy, ay, ref lo, ref hi) || !ClipExact(dy, height - ay, ref lo, ref hi)) return [];
        int startX = Pixel(ax, dx, lo), startY = Pixel(ay, dy, lo);
        int endX = Pixel(ax, dx, hi), endY = Pixel(ay, dy, hi);
        if (lo.CompareTo(hi) == 0 && (startX >= descriptor.Width || startY >= descriptor.Height)) return [];
        return Rasterize(Math.Min(startX, descriptor.Width - 1), Math.Min(startY, descriptor.Height - 1),
            Math.Min(endX, descriptor.Width - 1), Math.Min(endY, descriptor.Height - 1));

        int Pixel(BigInteger origin, BigInteger delta, Fraction t)
            => (int)((origin * t.Denominator + delta * t.Numerator) / (t.Denominator << shift));
    }

    private static int Exponent(double value)
    {
        if (value == 0) return 0;
        int encoded = (int)((BitConverter.DoubleToUInt64Bits(value) >> 52) & 0x7ff);
        return encoded == 0 ? -1074 : encoded - 1075;
    }

    private static BigInteger Scale(double value, int shift)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        ulong mantissa = bits & 0x000fffffffffffffUL;
        if ((bits & 0x7ff0000000000000UL) != 0) mantissa |= 1UL << 52;
        BigInteger scaled = (BigInteger)mantissa << (Exponent(value) + shift);
        return (bits >> 63) == 0 ? scaled : -scaled;
    }

    private readonly record struct Fraction(BigInteger Numerator, BigInteger Denominator)
    {
        internal int CompareTo(Fraction other)
            => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    private static bool ClipExact(BigInteger p, BigInteger q, ref Fraction lo, ref Fraction hi)
    {
        if (p.IsZero) return q.Sign >= 0;
        var t = p.Sign < 0 ? new Fraction(-q, -p) : new Fraction(q, p);
        if (p.Sign < 0)
        {
            if (t.CompareTo(hi) > 0) return false;
            if (t.CompareTo(lo) > 0) lo = t;
        }
        else
        {
            if (t.CompareTo(lo) < 0) return false;
            if (t.CompareTo(hi) < 0) hi = t;
        }
        return true;
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
