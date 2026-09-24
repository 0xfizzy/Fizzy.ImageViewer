using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements.Presentation;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single owner for preview, model, presentation, query and external resources.</summary>
internal sealed class MeasurementItem : IMeasurement
{
    private readonly MeasurementCollection _owner;
    private readonly MeasurementRuntime _runtime;
    internal MeasurementPresentation Presentation { get; }
    internal MeasurementCreationContext Session { get; }
    private readonly MeasurementOptions _options;
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private QuerySubscription? _subscription;
    internal MeasurementQueryClient QueryClient { get; }
    public MeasurementOrigin Origin { get; }
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
    MeasurementGeometry IMeasurement.Geometry => _runtime.Invoke(() => Geometry);
    long IMeasurement.GeometryVersion => _runtime.Invoke(() => GeometryVersion);
    MeasurementResult? IMeasurement.Result => _runtime.Invoke(() => Result);
    bool IMeasurement.IsComplete => _runtime.Invoke(() => IsComplete);
    void IMeasurement.UpdateGeometry(MeasurementGeometry geometry) => _runtime.Invoke(() => UpdateGeometry(geometry));
    void IMeasurement.Complete() => _runtime.Invoke(Complete);
    void IMeasurement.AddResource(IDisposable resource) => _runtime.Invoke(() => AddResource(resource));
    void IMeasurement.OnDispose(Action callback) => _runtime.Invoke(() => OnDispose(callback));
    void IDisposable.Dispose() => _runtime.InvokeRemoval(Dispose);
    event Action<MeasurementGeometry>? IMeasurement.GeometryChanged
    {
        add => _runtime.Invoke(() => { EnsureAlive(); GeometryChanged += value; });
        remove => _runtime.InvokeRemoval(() => GeometryChanged -= value);
    }
    event Action<MeasurementResult?>? IMeasurement.ResultChanged
    {
        add => _runtime.Invoke(() => { EnsureAlive(); ResultChanged += value; });
        remove => _runtime.InvokeRemoval(() => ResultChanged -= value);
    }

    internal MeasurementItem(MeasurementCollection owner, MeasurementRuntime runtime, MeasurementOverlay layer, MeasurementStyle style, MeasurementGeometry geometry, MeasurementOptions options, MeasurementCreationContext session)
    {
        _owner = owner;
        _runtime = runtime;
        Geometry = geometry;
        Session = session;
        Origin = session.Origin;
        _options = options with { Style = null };
        QueryClient = new(this, options.Query.Kind);
        Presentation = new(layer, geometry, style, () => runtime.RunUiCallback(Dispose));
    }
    private void EnsureAlive()
    {
        _runtime.VerifyAccess();
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
        using var notificationScope = _runtime.Notifications.Defer();
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
        if (!IsDisposed && GeometryVersion == version) _runtime.Notifications.Notify(GeometryChanged, geometry);
    }
    public void Complete()
    {
        using var notificationScope = _runtime.Notifications.Defer();
        EnsureAlive();
        if (IsComplete) return;
        IsComplete = true;
        Session.Release(this);
        try
        {
            Presentation.Complete(_options.Query is LineProfileMeasurementQueryOptions { ShowWindow: true });
            if (!IsDisposed && _options.Query.Kind != MeasurementQuery.None) _subscription = _runtime.Register(QueryClient);
            if (!IsDisposed) _owner.NotifyCompleted(this);
        }
        catch (Exception error) { MeasurementFailure.RethrowAfterCleanup(error, Dispose); throw; }
    }
    public void ClearResult() => ClearResult(notifyChanged: true);

    private void ClearResult(bool notifyChanged)
    {
        using var notificationScope = _runtime.Notifications.Defer();
        var hadResult = Result != null;
        Result = null;
        Presentation.ClearResult(Geometry);
        if (hadResult && !IsDisposed)
        {
            if (notifyChanged) _owner.NotifyChanged(this);
            if (!IsDisposed && Result == null) _runtime.Notifications.Notify(ResultChanged, (MeasurementResult?)null);
        }
    }
    internal void PublishResult(MeasurementResult result)
    {
        using var notificationScope = _runtime.Notifications.Defer();
        if (IsDisposed || result.GeometryVersion != GeometryVersion) return;
        Result = result;
        Presentation.ShowResult(Geometry, result);
        _owner.NotifyChanged(this);
        if (!IsDisposed && ReferenceEquals(Result, result)) _runtime.Notifications.Notify(ResultChanged, result);
    }

    public void Dispose()
    {
        using var notificationScope = _runtime.Notifications.Defer();
        _runtime.VerifyAccess();
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
