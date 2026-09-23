using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements.BuiltIn;
using System.Windows;
using System.Windows.Controls;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single owner for preview, model, presentation, query and external resources.</summary>
internal sealed class MeasurementItem : IMeasurement, IFrameQueryClient
{
    private readonly MeasurementContext _context;
    private readonly MeasurementDisplayAdapter _display;
    private readonly MeasurementOptions _options;
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private QuerySubscription? _subscription;
    private LineProfilePlotView? _plot;
    private QueryRequest? _cached;
    private (long Version, FrameDescriptor Descriptor)? _cacheKey;
    private PixelCoordinate[] _coordinates = [];
    private PixelSample[] _samples = [];
    private ChannelStatistics[] _channels = [];
    private PixelRegion? _region;
    private LineProfile? _profile;
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public long GeometryVersion { get; private set; }
    public MeasurementResult? Result { get; private set; }
    public UIElement PrimaryVisual { get; }
    public TextBlock Label { get; }
    public bool IsDisposed { get; private set; }
    public bool IsComplete { get; private set; }
    internal bool CompletionNotified { get; set; }
    public IEnumerable<UIElement> Visuals => [PrimaryVisual, Label];
    public event Action<MeasurementGeometry>? GeometryChanged;
    public event Action<MeasurementResult?>? ResultChanged;

    internal MeasurementItem(MeasurementContext context, MeasurementGeometry geometry, MeasurementOptions options)
    {
        _context = context; Geometry = geometry; _options = options with { Style = null };
        var style = (options.Style ?? context.Style).Snapshot();
        PrimaryVisual = geometry.Kind switch
        {
            ShapeType.Point => Shapes.CreatePoint(geometry.Start, style),
            ShapeType.Crosshair => Shapes.CreateCrosshair(geometry.Start, style: style),
            ShapeType.Line => Shapes.CreateLine(style),
            ShapeType.Rectangle => Shapes.CreateRectangle(style),
            ShapeType.Circle => Shapes.CreateCircle(geometry.Start, geometry.Radius, style),
            _ => throw new ArgumentException("Unsupported geometry.", nameof(geometry))
        };
        Label = Shapes.CreateLabel(geometry.Start, "", 5, 0, style);
        _display = new(context, PrimaryVisual, Label);
        _display.Apply(geometry); UpdateText();
    }
    private void EnsureAlive() { _context.VerifyAccess(); ObjectDisposedException.ThrowIf(IsDisposed, this); }
    public void AddResource(IDisposable resource) { EnsureAlive(); ArgumentNullException.ThrowIfNull(resource); _resources.Add(resource); }
    public void OnDispose(Action callback) { EnsureAlive(); ArgumentNullException.ThrowIfNull(callback); _callbacks.Add(callback); }
    public void UpdateGeometry(MeasurementGeometry geometry)
    {
        EnsureAlive(); ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Kind != Geometry.Kind) throw new ArgumentException("Geometry kind cannot change.", nameof(geometry));
        if (geometry == Geometry) return;
        Geometry = geometry; GeometryVersion++;
        _cached = null; _cacheKey = null;
        _display.Apply(geometry);
        ClearResult();
        if (!IsDisposed && Geometry == geometry) _context.Notify(GeometryChanged, geometry);
    }
    public void Complete()
    {
        EnsureAlive(); if (IsComplete) return;
        IsComplete = true;
        try
        {
            if (_options.ShowLineProfile)
            {
                _plot = new LineProfilePlotView();
                _plot.Window.Closed += PlotClosed;
                _plot.Window.Show();
            }
            if (!IsDisposed && _options.Query != MeasurementQuery.None) _subscription = _context.Register(this);
            if (!IsDisposed) _context.NotifyCompleted(this);
        }
        catch { Dispose(); throw; }
    }
    private void PlotClosed(object? sender, EventArgs args) => Dispose();
    private void UpdateText() => Label.Text = Geometry.Kind switch
    {
        ShapeType.Line => $"{(Geometry.End - Geometry.Start).Length:F1} px",
        ShapeType.Point or ShapeType.Crosshair => $"X:{Geometry.X:F2}\nY:{Geometry.Y:F2}",
        ShapeType.Circle => $"r={Geometry.Radius:F1} px",
        _ => $"{Geometry.Width:F1} × {Geometry.Height:F1} px"
    };
    public void ClearResult()
    {
        var hadResult = Result != null;
        Result = null; _samples = []; _channels = []; _region = null;
        UpdateText(); _plot?.Clear();
        if (hadResult && !IsDisposed) _context.Notify(ResultChanged, (MeasurementResult?)null);
    }
    public QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (IsDisposed || !IsComplete || _options.Query == MeasurementQuery.None) return null;
        var key = (GeometryVersion, descriptor);
        if (_cacheKey == key) return _cached;
        _cacheKey = key;
        var identity = new QueryIdentity(Id, GeometryVersion);
        switch (_options.Query)
        {
            case MeasurementQuery.RegionStatistics:
                var region = Geometry.ToRegion(descriptor);
                return _cached = region.IsEmpty ? null : new RegionStatisticsQueryRequest(identity, region, stats =>
                { _region = region; _channels = (ChannelStatistics[])stats.Channels.Clone(); });
            case MeasurementQuery.Pixel:
                var x = Math.Floor(Geometry.Start.X); var y = Math.Floor(Geometry.Start.Y);
                if (x < 0 || y < 0 || x >= descriptor.Width || y >= descriptor.Height) return _cached = null;
                _coordinates = [new((int)x, (int)y)];
                return _cached = new PixelQueryRequest(identity, _coordinates, samples => _samples = samples.ToArray());
            case MeasurementQuery.LineProfile:
                _profile = new LineProfile();
                _coordinates = _profile.Prepare(descriptor, Geometry.Start.X, Geometry.Start.Y, Geometry.End.X, Geometry.End.Y);
                return _cached = _coordinates.Length == 0 ? null : new LineProfileQueryRequest(identity, _coordinates,
                    samples => _samples = samples.ToArray());
            default: return null;
        }
    }
    public void ResultPublished(FrameInfo frame)
    {
        if (IsDisposed) return;
        UpdateText();
        Result = new(Id, GeometryVersion, frame, _options.Query, _coordinates, _samples, _region, _channels);
        if (_options.Query == MeasurementQuery.Pixel && _samples.Length > 0) Label.Text += $" | {_samples[0]}";
        else if (_options.Query == MeasurementQuery.RegionStatistics && _region is { } region)
        {
            var names = _channels.Length == 1 ? new[] { "Gray" } : new[] { "R", "G", "B", "A" };
            Label.Text = $"{region.Width} × {region.Height} px | " + string.Join(" | ", _channels.Select((s, i) =>
                $"{names[i]}: n={s.Count} min={s.Minimum:G7} max={s.Maximum:G7} mean={s.Mean:G7}"));
        }
        else if (_options.Query == MeasurementQuery.LineProfile && _profile != null)
        { _profile.Apply(_samples); _plot?.ShowProfile(_profile); }
        _context.Notify(ResultChanged, Result);
    }
    public void Dispose()
    {
        _context.VerifyAccess(); if (IsDisposed) return; IsDisposed = true;
        List<Exception> errors = [];
        void Release(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        Release(() => _subscription?.Dispose()); _subscription = null;
        foreach (var callback in _callbacks) Release(callback);
        foreach (var resource in _resources) Release(resource.Dispose);
        var plot = _plot; _plot = null;
        if (plot != null) Release(() => { plot.Window.Closed -= PlotClosed; plot.Window.Close(); });
        Release(() => _context.Detach(this));
        _callbacks.Clear(); _resources.Clear(); _cached = null; _profile = null;
        Result = null; _samples = []; _channels = []; _coordinates = [];
        GeometryChanged = null; ResultChanged = null;
        if (errors.Count > 0) throw new AggregateException("Measurement cleanup failed.", errors);
    }
}
