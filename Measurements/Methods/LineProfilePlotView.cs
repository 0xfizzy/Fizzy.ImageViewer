using System.Windows;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Measurements.Methods;

/// <summary>UI-thread presentation of line samples. MeasurementItem owns the window lifetime.</summary>
internal sealed class LineProfilePlotView
{
    private static readonly ScottPlot.Color ScottRed = ScottPlot.Color.FromColor(System.Drawing.Color.Red);
    private static readonly ScottPlot.Color ScottGreen = ScottPlot.Color.FromColor(System.Drawing.Color.Green);
    private static readonly ScottPlot.Color ScottBlue = ScottPlot.Color.FromColor(System.Drawing.Color.Blue);
    private double[] _xs = [], _rs = [], _gs = [], _bs = [];
    private ScottPlot.Plottables.Scatter? _red, _green, _blue;

    public Window Window { get; } = new()
    {
        Title = "Pixel Values", Width = 600, Height = 400, Topmost = true,
        Content = new ScottPlot.WPF.WpfPlot()
    };

    public void Clear()
    {
        if (_red != null) _red.IsVisible = false;
        if (_green != null) _green.IsVisible = false;
        if (_blue != null) _blue.IsVisible = false;
        ((ScottPlot.WPF.WpfPlot)Window.Content).Refresh();
    }

    public void ShowProfile(LineProfile profile)
    {
        var plot = (ScottPlot.WPF.WpfPlot)Window.Content;
        int count = profile.Count;
        if (count > _xs.Length)
        {
            _xs = new double[count]; _rs = new double[count]; _gs = new double[count]; _bs = new double[count];
            plot.Plot.Clear();
            _red = plot.Plot.Add.Scatter(_xs, _rs, ScottRed);
            _green = plot.Plot.Add.Scatter(_xs, _gs, ScottGreen);
            _blue = plot.Plot.Add.Scatter(_xs, _bs, ScottBlue);
        }
        if (_red != null)
        {
            Array.Copy(profile.Distances, _xs, count); Array.Copy(profile.Red, _rs, count);
            Array.Copy(profile.Green, _gs, count); Array.Copy(profile.Blue, _bs, count);
            // Non-finite raw samples remain available through the reader; plots use gaps.
            for (int i = 0; i < count; i++)
            {
                if (!double.IsFinite(_rs[i])) _rs[i] = double.NaN;
                if (!double.IsFinite(_gs[i])) _gs[i] = double.NaN;
                if (!double.IsFinite(_bs[i])) _bs[i] = double.NaN;
            }
            _red.IsVisible = count > 0;
            _green!.IsVisible = _blue!.IsVisible = count > 0 && !profile.IsGray;
            if (count > 0)
            {
                _red.Data.MaxRenderIndex = _green.Data.MaxRenderIndex = _blue.Data.MaxRenderIndex = count - 1;
                plot.Plot.Axes.AutoScale();
            }
        }
        Window.Title = "Pixel Values";
        plot.Refresh();
    }
}
