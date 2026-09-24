using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements.Presentation;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single owner for preview, model, presentation, query and external resources.</summary>
internal sealed class MeasurementItem : IMeasurement
{
    private readonly MeasurementCollection _owner;
    internal MeasurementPresentation Presentation { get; }
    internal MeasurementCreationSession Session { get; }
    private readonly MeasurementOptions _options;
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private QuerySubscription? _subscription;
    internal MeasurementQueryClient QueryClient { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public long GeometryVersion { get; private set; }
    public MeasurementResult? Result { get; private set; }
    private volatile bool _disposed;
    public bool IsDisposed => _disposed;
    public bool IsComplete { get; private set; }
    internal bool CompletionNotified { get; set; }
    internal event Action<MeasurementGeometry>? GeometryApplied;
    public event Action<MeasurementGeometry>? GeometryChanged;
    public event Action<MeasurementResult?>? ResultChanged;

    // Public handles dispatch; model, query and cleanup paths already own the STA.
    MeasurementGeometry IMeasurement.Geometry => _owner.Invoke(() => Geometry);
    long IMeasurement.GeometryVersion => _owner.Invoke(() => GeometryVersion);
    MeasurementResult? IMeasurement.Result => _owner.Invoke(() => Result);
    bool IMeasurement.IsComplete => _owner.Invoke(() => IsComplete);
    void IMeasurement.UpdateGeometry(MeasurementGeometry geometry) => _owner.Invoke(() => UpdateGeometry(geometry));
    void IMeasurement.Complete() => _owner.Invoke(Complete);
    void IMeasurement.AddResource(IDisposable resource) => _owner.Invoke(() => AddResource(resource));
    void IMeasurement.OnDispose(Action callback) => _owner.Invoke(() => OnDispose(callback));
    void IDisposable.Dispose() => _owner.InvokeRemoval(Dispose);
    event Action<MeasurementGeometry>? IMeasurement.GeometryChanged
    {
        add => _owner.Invoke(() => { EnsureAlive(); GeometryChanged += value; });
        remove => _owner.InvokeRemoval(() => GeometryChanged -= value);
    }
    event Action<MeasurementResult?>? IMeasurement.ResultChanged
    {
        add => _owner.Invoke(() => { EnsureAlive(); ResultChanged += value; });
        remove => _owner.InvokeRemoval(() => ResultChanged -= value);
    }

    internal MeasurementItem(MeasurementCollection owner, MeasurementGeometry geometry, MeasurementOptions options, MeasurementCreationSession session)
    {
        _owner = owner;
        Geometry = geometry;
        Session = session;
        _options = options with { Style = null };
        QueryClient = new(this, options.Query);
        var style = (options.Style ?? owner.Style).Snapshot();
        Presentation = new(owner.Layer, geometry, style, Dispose);
    }
    private void EnsureAlive()
    {
        _owner.VerifyAccess();
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
        QueryClient.Reset();
        Presentation.Apply(geometry);
        GeometryApplied?.Invoke(geometry);
        ClearResult(notifyChanged: false);
        if (IsDisposed || GeometryVersion != version) return;
        _owner.NotifyChanged(this);
        if (!IsDisposed && GeometryVersion == version) _owner.Notify(GeometryChanged, geometry);
    }
    public void Complete()
    {
        EnsureAlive();
        if (IsComplete) return;
        IsComplete = true;
        Session.Release(this);
        try
        {
            Presentation.Complete(_options.ShowProfileWindow);
            if (!IsDisposed && _options.Query != MeasurementQuery.None) _subscription = _owner.Register(QueryClient);
            if (!IsDisposed) _owner.NotifyCompleted(this);
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
            if (notifyChanged) _owner.NotifyChanged(this);
            if (!IsDisposed && Result == null) _owner.Notify(ResultChanged, (MeasurementResult?)null);
        }
    }
    internal void PublishResult(MeasurementResult result)
    {
        if (IsDisposed || result.GeometryVersion != GeometryVersion) return;
        Result = result;
        Presentation.ShowResult(Geometry, result);
        _owner.NotifyChanged(this);
        if (!IsDisposed && ReferenceEquals(Result, result)) _owner.Notify(ResultChanged, result);
    }

    public void Dispose()
    {
        _owner.VerifyAccess();
        if (IsDisposed) return;
        _disposed = true;
        Session.Release(this);
        List<Exception> errors = [];
        void Release(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        Release(() => _subscription?.Dispose());
        _subscription = null;
        foreach (var callback in _callbacks) Release(callback);
        foreach (var resource in _resources) Release(resource.Dispose);
        Release(() => _owner.Detach(this));
        Release(Presentation.Dispose);
        Release(() => _owner.NotifyRemoved(this));
        _callbacks.Clear();
        _resources.Clear();
        QueryClient.Reset();
        Result = null;
        GeometryApplied = null;
        GeometryChanged = null;
        ResultChanged = null;
        if (errors.Count > 0) throw new AggregateException("Measurement cleanup failed.", errors);
    }
}
