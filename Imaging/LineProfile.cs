using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Imaging;

internal sealed class LineProfile
{
    public double[] Distances { get; private set; } = [];
    public double[] Red { get; private set; } = [];
    public double[] Green { get; private set; } = [];
    public double[] Blue { get; private set; } = [];
    public int Count { get; private set; }
    public bool IsGray { get; private set; }
    public PixelCoordinate[] Prepare(FrameDescriptor descriptor, double x0, double y0, double x1, double y1)
    {
        Count = 0;
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)) return [];
        double originalX = x0, originalY = y0, dx = x1 - x0, dy = y1 - y0;
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
        if (Distances.Length < count)
        {
            Distances = new double[count]; Red = new double[count]; Green = new double[count]; Blue = new double[count];
        }
        var coordinates=new PixelCoordinate[count];
        long err = (long)nx - ny;
        for (int i = 0; i < count; i++)
        {
            coordinates[i]=new(ax,ay);
            Distances[i] = Math.Sqrt(Math.Pow(ax - originalX, 2) + Math.Pow(ay - originalY, 2));

            if (ax == bx && ay == by) { Count = i + 1; break; }
            long e = 2 * err;
            if (e > -ny) { err -= ny; ax += sx; }
            if (e < nx) { err += nx; ay += sy; }
        }
        return coordinates;
    }
    public void Apply(PixelSample[] samples)
    {
        for(int i=0;i<samples.Length;i++) { var p=samples[i]; IsGray=p.IsGrayscale; Red[i]=p.IsGrayscale?p.Gray:p.R; Green[i]=p.G; Blue[i]=p.B; }
        Count=samples.Length;
    }
    public async ValueTask SampleAsync(FrameLease frame,double x0,double y0,double x1,double y1,CancellationToken ct)
    {
        var points=Prepare(frame.Descriptor,x0,y0,x1,y1);
        Apply((await frame.ReadPixelsAsync(points,ct)).Samples);
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
