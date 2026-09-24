using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Measurements.Presentation;

/// <summary>Owns measurement visuals, labels and the optional plot; model changes flow into presentation.</summary>
internal sealed class MeasurementPresentation : IDisposable
{
    private readonly MeasurementOverlay _layer;
    private readonly Action _closed;
    private LineProfilePlotView? _plot;
    private bool _disposed;
    internal IEnumerable<UIElement> Visuals => [PrimaryVisual, Label];
    public UIElement PrimaryVisual { get; }
    public TextBlock Label { get; }

    internal MeasurementPresentation(MeasurementOverlay layer, MeasurementGeometry geometry, MeasurementStyle style, Action closed)
    {
        _layer = layer;
        _closed = closed;
        PrimaryVisual = geometry switch
        {
            PointMeasurementGeometry point => MeasurementVisualFactory.CreatePoint(point.Position, style),
            CrosshairMeasurementGeometry crosshair => MeasurementVisualFactory.CreateCrosshair(crosshair.Position, style: style),
            LineMeasurementGeometry => MeasurementVisualFactory.CreateLine(style),
            RectangleMeasurementGeometry => MeasurementVisualFactory.CreateRectangle(style),
            CircleMeasurementGeometry circle => MeasurementVisualFactory.CreateCircle(circle.Center, circle.Radius, style),
            _ => throw new ArgumentException("Unsupported geometry.", nameof(geometry))
        };
        Label = MeasurementVisualFactory.CreateLabel(geometry.Anchor, "", 5, 0, style);
        Apply(geometry);
        ClearResult(geometry);
    }

    internal void Attach()
    {
        foreach (var visual in Visuals)
        {
            if (_disposed) break;
            _layer.AddVisual(visual);
        }
    }

    internal void Complete(bool showProfileWindow)
    {
        if (!showProfileWindow) return;
        _plot = new LineProfilePlotView();
        _plot.Window.Closed += PlotClosed;
        _plot.Window.Show();
    }

    private void PlotClosed(object? sender, EventArgs args) => _closed();

    private void UpdateText(MeasurementGeometry geometry) => Label.Text = geometry switch
    {
        LineMeasurementGeometry line => $"{line.Length:F1} px",
        PointMeasurementGeometry point => PositionText(point.Position),
        CrosshairMeasurementGeometry crosshair => PositionText(crosshair.Position),
        CircleMeasurementGeometry circle => $"r={circle.Radius:F1} px",
        _ => $"{geometry.Bounds.Width:F1} × {geometry.Bounds.Height:F1} px"
    };
    private static string PositionText(System.Windows.Point point) => $"X:{point.X:F2}\nY:{point.Y:F2}";

    internal void ClearResult(MeasurementGeometry geometry)
    {
        UpdateText(geometry);
        _plot?.Clear();
    }

    internal void ShowResult(MeasurementGeometry geometry, MeasurementQueryResult result)
    {
        UpdateText(geometry);
        if (result is MeasurementSampleResult { Query: MeasurementQueryKind.Pixel } pixel)
            Label.Text += $" | {pixel.Samples[0]}";
        else if (result is MeasurementRegionResult statistics)
        {
            var region = statistics.Region;
            var names = statistics.Channels.Count == 1 ? new[] { "Gray" } : new[] { "R", "G", "B", "A" };
            Label.Text = $"{region.Width} × {region.Height} px | " + string.Join(" | ", statistics.Channels.Select((s, i) =>
                $"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        }
        else if (result is MeasurementSampleResult { Query: MeasurementQueryKind.LineProfile } profile)
            _plot?.ShowProfile(profile.Samples);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var plot = _plot;
        _plot = null;
        try
        {
            if (plot != null)
            {
                plot.Window.Closed -= PlotClosed;
                plot.Window.Close();
            }
        }
        finally
        {
            // Labels leave first so removal callbacks cannot see an orphaned label.
            try { _layer.RemoveVisual(Label); }
            finally { _layer.RemoveVisual(PrimaryVisual); }
        }
    }

    public void Apply(MeasurementGeometry geometry)
    {
        var shape = PrimaryVisual;
        switch (geometry)
        {
            case CircleMeasurementGeometry circle when shape is Path { Data: System.Windows.Media.EllipseGeometry ellipse }:
                ellipse.RadiusX = ellipse.RadiusY = circle.Radius;
                _layer.UpdateAnchor(shape, circle.Center);
                break;
            case RectangleMeasurementGeometry rectangle when shape is Rectangle visual:
                visual.Width = rectangle.Bounds.Width;
                visual.Height = rectangle.Bounds.Height;
                _layer.UpdateAnchor(visual, rectangle.TopLeft);
                break;
            case LineMeasurementGeometry line when shape is Line visual:
                visual.X1 = line.Start.X; visual.Y1 = line.Start.Y;
                visual.X2 = line.End.X; visual.Y2 = line.End.Y;
                break;
            default:
                _layer.UpdateAnchor(shape, geometry.Anchor);
                break;
        }
        _layer.UpdateAnchor(Label, geometry is LineMeasurementGeometry segment ? segment.End : geometry.Anchor);
    }
}
