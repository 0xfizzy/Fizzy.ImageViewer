using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements.Presentation;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single owner for preview, model, presentation, query and external resources.</summary>
internal sealed class MeasurementItem : IMeasurement, IFrameQueryClient
{
    private readonly MeasurementContext _context;
    internal MeasurementPresentation Presentation { get; }
    internal MeasurementCreationSession Session { get; }
    private readonly MeasurementOptions _options;
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private QuerySubscription? _subscription;
    private QueryRequest? _cached;
    private (long Version, FrameDescriptor Descriptor)? _cacheKey;
    private PixelCoordinate[] _coordinates = [];
    private LineProfile? _profile;
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public long GeometryVersion { get; private set; }
    public MeasurementResult? Result { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool IsComplete { get; private set; }
    internal bool CompletionNotified { get; set; }
    public event Action<MeasurementGeometry>? GeometryChanged;
    public event Action<MeasurementResult?>? ResultChanged;

    internal MeasurementItem(MeasurementContext context, MeasurementGeometry geometry, MeasurementOptions options, MeasurementCreationSession session)
    {
        _context = context;
        Geometry = geometry;
        Session = session;
        _options = options with { Style = null };
        var style = (options.Style ?? context.Style).Snapshot();
        Presentation = new(context.Layer, geometry, style, Dispose);
    }
    private void EnsureAlive()
    {
        _context.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void AddResource(IDisposable resource)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
    }

    public void OnDispose(Action callback)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
    }

    public void UpdateGeometry(MeasurementGeometry geometry)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Kind != Geometry.Kind) throw new ArgumentException("Geometry kind cannot change.", nameof(geometry));
        if (geometry == Geometry) return;
        Geometry = geometry;
        var version = ++GeometryVersion;
        _cached = null;
        _cacheKey = null;
        Presentation.Apply(geometry);
        ClearResult(notifyChanged: false);
        if (IsDisposed || GeometryVersion != version) return;
        _context.NotifyChanged(this);
        if (!IsDisposed && GeometryVersion == version) _context.Notify(GeometryChanged, geometry);
    }
    public void Complete()
    {
        EnsureAlive();
        if (IsComplete) return;
        IsComplete = true;
        Session.Release(this);
        try
        {
            Presentation.Complete(_options.ShowLineProfile);
            if (!IsDisposed && _options.Query != MeasurementQuery.None) _subscription = _context.Register(this);
            if (!IsDisposed) _context.NotifyCompleted(this);
        }
        catch { Dispose(); throw; }
    }
    public void ClearResult() => ClearResult(notifyChanged: true);

    private void ClearResult(bool notifyChanged)
    {
        var hadResult = Result != null;
        Result = null;
        Presentation.ClearResult(Geometry);
        if (hadResult && !IsDisposed)
        {
            if (notifyChanged) _context.NotifyChanged(this);
            if (!IsDisposed && Result == null) _context.Notify(ResultChanged, (MeasurementResult?)null);
        }
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
                return _cached = region.IsEmpty ? null : new RegionStatisticsQueryRequest(identity, region,
                    (frame, stats) => PublishResult(new(Id, identity.GeometryVersion, frame, _options.Query,
                        [], [], region, (ChannelStatistics[])stats.Channels.Clone())));
            case MeasurementQuery.Pixel:
                var x = Math.Floor(Geometry.Start.X);
                var y = Math.Floor(Geometry.Start.Y);
                if (x < 0 || y < 0 || x >= descriptor.Width || y >= descriptor.Height) return _cached = null;
                _coordinates = [new((int)x, (int)y)];
                return _cached = new PixelQueryRequest(identity, _coordinates, PublishSamples);
            case MeasurementQuery.LineProfile:
                _profile = new LineProfile();
                _coordinates = _profile.Prepare(descriptor, Geometry.Start.X, Geometry.Start.Y, Geometry.End.X, Geometry.End.Y);
                return _cached = _coordinates.Length == 0 ? null : new LineProfileQueryRequest(identity, _coordinates, PublishSamples);
            default: return null;
        }
    }
    private void PublishSamples(FrameInfo frame, ReadOnlySpan<PixelSample> samples)
    {
        if (IsDisposed) return;
        if (_options.Query == MeasurementQuery.LineProfile) _profile?.Apply(samples);
        PublishResult(new(Id, GeometryVersion, frame, _options.Query, _coordinates, samples.ToArray(), null, []));
    }

    private void PublishResult(MeasurementResult result)
    {
        if (IsDisposed) return;
        Result = result;
        Presentation.ShowResult(Geometry, result, _profile);
        _context.NotifyChanged(this);
        if (!IsDisposed && ReferenceEquals(Result, result)) _context.Notify(ResultChanged, result);
    }

    public void Dispose()
    {
        _context.VerifyAccess();
        if (IsDisposed) return;
        IsDisposed = true;
        Session.Release(this);
        List<Exception> errors = [];
        void Release(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        Release(() => _subscription?.Dispose());
        _subscription = null;
        foreach (var callback in _callbacks) Release(callback);
        foreach (var resource in _resources) Release(resource.Dispose);
        Release(() => _context.Detach(this));
        _callbacks.Clear();
        _resources.Clear();
        _cached = null;
        _profile = null;
        Result = null;
        _coordinates = [];
        GeometryChanged = null;
        ResultChanged = null;
        if (errors.Count > 0) throw new AggregateException("Measurement cleanup failed.", errors);
    }
}
