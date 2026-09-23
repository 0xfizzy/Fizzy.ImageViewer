using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Fizzy.ImageViewer.Measurements.Presentation;

/// <summary>Owns measurement visuals, labels and the optional plot; model changes flow into presentation.</summary>
internal sealed class MeasurementPresentation : IDisposable
{
    private readonly OverlayLayer _layer;
    private readonly Action _closed;
    private LineProfilePlotView? _plot;
    public UIElement PrimaryVisual { get; }
    public TextBlock Label { get; }

    internal MeasurementPresentation(OverlayLayer layer, MeasurementGeometry geometry, ShapeStyle style, Action closed)
    {
        _layer = layer;
        _closed = closed;
        PrimaryVisual = geometry.Kind switch
        {
            MeasurementKind.Point => MeasurementVisualFactory.CreatePoint(geometry.Start, style),
            MeasurementKind.Crosshair => MeasurementVisualFactory.CreateCrosshair(geometry.Start, style: style),
            MeasurementKind.Line => MeasurementVisualFactory.CreateLine(style),
            MeasurementKind.Rectangle => MeasurementVisualFactory.CreateRectangle(style),
            MeasurementKind.Circle => MeasurementVisualFactory.CreateCircle(geometry.Start, geometry.Radius, style),
            _ => throw new ArgumentException("Unsupported geometry.", nameof(geometry))
        };
        Label = MeasurementVisualFactory.CreateLabel(geometry.Start, "", 5, 0, style);
        Apply(geometry);
        ClearResult(geometry);
    }

    internal void Complete(bool showLineProfile)
    {
        if (!showLineProfile) return;
        _plot = new LineProfilePlotView();
        _plot.Window.Closed += PlotClosed;
        _plot.Window.Show();
    }

    private void PlotClosed(object? sender, EventArgs args) => _closed();

    private void UpdateText(MeasurementGeometry geometry) => Label.Text = geometry.Kind switch
    {
        MeasurementKind.Line => $"{(geometry.End - geometry.Start).Length:F1} px",
        MeasurementKind.Point or MeasurementKind.Crosshair => $"X:{geometry.X:F2}\nY:{geometry.Y:F2}",
        MeasurementKind.Circle => $"r={geometry.Radius:F1} px",
        _ => $"{geometry.Bounds.Width:F1} × {geometry.Bounds.Height:F1} px"
    };

    internal void ClearResult(MeasurementGeometry geometry)
    {
        UpdateText(geometry);
        _plot?.Clear();
    }

    internal void ShowResult(MeasurementGeometry geometry, MeasurementResult result, LineProfile? profile)
    {
        UpdateText(geometry);
        if (result.Query == MeasurementQuery.Pixel && result.Samples.Count > 0)
            Label.Text += $" | {result.Samples[0]}";
        else if (result.Query == MeasurementQuery.RegionStatistics && result.Region is { } region)
        {
            var names = result.Channels.Count == 1 ? new[] { "Gray" } : new[] { "R", "G", "B", "A" };
            Label.Text = $"{region.Width} × {region.Height} px | " + string.Join(" | ", result.Channels.Select((s, i) =>
                $"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        }
        else if (result.Query == MeasurementQuery.LineProfile && profile != null)
            _plot?.ShowProfile(profile);
    }

    public void Dispose()
    {
        var plot = _plot;
        _plot = null;
        if (plot == null) return;
        plot.Window.Closed -= PlotClosed;
        plot.Window.Close();
    }

    public void Apply(MeasurementGeometry geometry)
    {
        var shape = PrimaryVisual;
        if (geometry.Kind == MeasurementKind.Circle && shape is Path path && path.Data is System.Windows.Media.EllipseGeometry ellipse)
        { ellipse.RadiusX = ellipse.RadiusY = geometry.Radius; }
        switch (shape)
        {
            case Rectangle rectangle:
                rectangle.Width = geometry.Bounds.Width;
                rectangle.Height = geometry.Bounds.Height;
                _layer.UpdateAnchor(rectangle, geometry.Start);
                break;
            case Line line:
                line.X1 = geometry.Start.X; line.Y1 = geometry.Start.Y;
                line.X2 = geometry.End.X; line.Y2 = geometry.End.Y;
                break;
            default:
                _layer.UpdateAnchor(shape, geometry.Start);
                break;
        }
        _layer.UpdateAnchor(Label, geometry.Kind == MeasurementKind.Line ? geometry.End : geometry.Start);
    }
}
