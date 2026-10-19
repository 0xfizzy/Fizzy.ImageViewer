using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Measurements.Presentation;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>The single owner for preview, model, presentation, query and external resources.</summary>
internal sealed class MeasurementItem : IMeasurement
{
    private MeasurementCollection? _owner;
    private MeasurementRuntime? _activeRuntime;
    private readonly ViewerLifetime _lifetime;
    private MeasurementRuntime _runtime => _activeRuntime ?? throw new ObjectDisposedException(nameof(MeasurementItem));
    internal MeasurementPresentation Presentation { get; }
    private MeasurementCreationContext? _creationContext;
    private readonly MeasurementOptions _options;
    private readonly List<IDisposable> _resources = [];
    private readonly List<Action> _callbacks = [];
    private QuerySubscription? _subscription;
    internal MeasurementQueryClient QueryClient { get; }
    public MeasurementOrigin Origin { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public long GeometryVersion { get; private set; }
    public MeasurementQueryResult? QueryResult { get; private set; }
    private volatile bool _disposed;
    public bool IsDisposed => _disposed;
    public bool IsComplete { get; private set; }
    internal bool CompletionNotified { get; set; }
    internal event Action<MeasurementGeometry>? GeometryApplied;
    public event EventHandler<MeasurementGeometry>? GeometryChanged;
    public event EventHandler<MeasurementQueryResult?>? QueryResultChanged;

    // Public handles dispatch; model, query and cleanup paths already own the STA.
    MeasurementGeometry IMeasurement.Geometry => Read(() => Geometry);
    long IMeasurement.GeometryVersion => Read(() => GeometryVersion);
    MeasurementQueryResult? IMeasurement.QueryResult => Read(() => QueryResult);
    bool IMeasurement.IsComplete => Read(() => IsComplete);
    void IMeasurement.UpdateGeometry(MeasurementGeometry geometry) => _runtime.Invoke(() => UpdateGeometry(geometry));
    void IMeasurement.Complete() => _runtime.Invoke(Complete);
    void IMeasurement.AddResource(IDisposable resource) => _runtime.Invoke(() => AddResource(resource));
    void IMeasurement.OnDispose(Action callback) => _runtime.Invoke(() => OnDispose(callback));
    void IDisposable.Dispose() => Volatile.Read(ref _activeRuntime)?.InvokeRemoval(Dispose);
    event EventHandler<MeasurementGeometry>? IMeasurement.GeometryChanged
    {
        add => _runtime.Invoke(() => { EnsureAlive(); GeometryChanged += value; });
        remove => Volatile.Read(ref _activeRuntime)?.InvokeRemoval(() => GeometryChanged -= value);
    }
    event EventHandler<MeasurementQueryResult?>? IMeasurement.QueryResultChanged
    {
        add => _runtime.Invoke(() => { EnsureAlive(); QueryResultChanged += value; });
        remove => Volatile.Read(ref _activeRuntime)?.InvokeRemoval(() => QueryResultChanged -= value);
    }

    internal MeasurementItem(MeasurementCollection owner, MeasurementRuntime runtime, MeasurementOverlay layer, MeasurementStyle style, MeasurementGeometry geometry, MeasurementOptions options, MeasurementCreationContext creationContext)
    {
        _owner = owner;
        _activeRuntime = runtime;
        _lifetime = runtime.Lifetime;
        Geometry = geometry;
        _creationContext = creationContext;
        Origin = creationContext.Origin;
        _options = options with { Style = null };
        QueryClient = new(this, options.Query);
        Presentation = new(layer, geometry, style, () => runtime.RunUiCallback(Dispose));
    }
    private T Read<T>(Func<T> read)
    {
        var runtime = Volatile.Read(ref _activeRuntime);
        if (runtime != null) return runtime.Invoke(read);
        // Disposed model values are stable, but their readability still ends with the Viewer.
        _lifetime.ThrowIfStopping();
        return read();
    }

    private void ReleaseCreationContext()
    {
        var context = _creationContext;
        _creationContext = null;
        context?.Release(this);
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
        if (_resources.Any(existing => ReferenceEquals(existing, resource)))
            throw new ArgumentException("The resource is already owned by this measurement.", nameof(resource));
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
        var runtime = _runtime;
        using var notificationScope = runtime.Notifications.Defer();
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Kind != Geometry.Kind) throw new ArgumentException("Geometry kind cannot change.", nameof(geometry));
        if (geometry == Geometry) return;
        Geometry = geometry;
        var version = ++GeometryVersion;
        QueryClient.Reset();
        Presentation.Apply(geometry);
        GeometryApplied?.Invoke(geometry);
        ClearQueryResult(notifyChanged: false);
        if (IsDisposed || GeometryVersion != version) return;
        _owner!.NotifyChanged(this);
        if (!IsDisposed && GeometryVersion == version) runtime.Notifications.Notify(GeometryChanged, this, geometry);
    }
    public void Complete()
    {
        var runtime = _runtime;
        using var notificationScope = runtime.Notifications.Defer();
        EnsureAlive();
        if (IsComplete) return;
        IsComplete = true;
        ReleaseCreationContext();
        try
        {
            Presentation.Complete(_options.ShowProfileWindow);
            if (!IsDisposed && _options.Query != MeasurementQueryKind.None) _subscription = _runtime.Register(QueryClient);
            if (!IsDisposed) _owner!.NotifyCompleted(this);
        }
        catch (Exception error) { MeasurementFailure.RethrowAfterCleanup(error, Dispose); throw; }
    }
    public void ClearQueryResult() => ClearQueryResult(notifyChanged: true);

    private void ClearQueryResult(bool notifyChanged)
    {
        if (IsDisposed) return;
        var runtime = _runtime;
        using var notificationScope = runtime.Notifications.Defer();
        var hadResult = QueryResult != null;
        QueryResult = null;
        Presentation.ClearResult(Geometry);
        if (hadResult && !IsDisposed)
        {
            if (notifyChanged) _owner!.NotifyChanged(this);
            if (!IsDisposed && QueryResult == null) runtime.Notifications.Notify(QueryResultChanged, this, (MeasurementQueryResult?)null);
        }
    }
    internal void PublishQueryResult(MeasurementQueryResult result)
    {
        if (IsDisposed) return;
        var runtime = _runtime;
        using var notificationScope = runtime.Notifications.Defer();
        if (IsDisposed || result.GeometryVersion != GeometryVersion) return;
        QueryResult = result;
        Presentation.ShowResult(Geometry, result);
        _owner!.NotifyChanged(this);
        if (!IsDisposed && ReferenceEquals(QueryResult, result)) runtime.Notifications.Notify(QueryResultChanged, this, result);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        var runtime = _runtime;
        using var notificationScope = runtime.Notifications.Defer();
        _runtime.VerifyAccess();
        _disposed = true;
        ReleaseCreationContext();
        List<Exception> errors = [];
        void Release(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        Release(() => _subscription?.Dispose());
        _subscription = null;
        foreach (var callback in _callbacks) Release(callback);
        foreach (var resource in _resources) Release(resource.Dispose);
        Release(() => _owner!.Detach(this));
        Release(Presentation.Dispose);
        Release(() => _owner!.NotifyRemoved(this));
        _owner = null;
        _callbacks.Clear();
        _resources.Clear();
        QueryClient.Reset();
        QueryResult = null;
        GeometryApplied = null;
        GeometryChanged = null;
        QueryResultChanged = null;
        Volatile.Write(ref _activeRuntime, null);
        if (errors.Count > 0) throw new AggregateException("Measurement cleanup failed.", errors);
    }
}
