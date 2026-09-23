using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Tool registry and measurement session execution, independent of input state.</summary>
internal sealed class MeasureManager : IDisposable
{
    internal sealed record Registration(string Id, string DisplayName, IMeasureTool Tool);
    private readonly Dictionary<string, Registration> _methods = new(StringComparer.Ordinal);
    private IMeasureTool? _active;
    private bool _disposed;
    internal long SessionVersion { get; private set; }
    public Registration[] RegisteredTools => _methods.Values.ToArray();
    public string? ActiveId { get; private set; }
    public bool IsMeasuring => _active != null;
    public MeasureContext Context { get; }
    internal MeasureManager(OverlayLayer output, Func<FrameLease?> acquire, PixelQueryScheduler scheduler, ILogger logger) => Context = new(output, acquire, scheduler, logger);
    public void RegisterMethod(IMeasureMethod method)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(method);
        RegisterTool(new CustomMeasureTool(method, Context));
    }
    internal void RegisterTool(IMeasureTool method)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(method);
        var id = method.Id; var name = method.DisplayName;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_methods.TryAdd(id, new(id, name, method))) throw new ArgumentException($"Measurement tool '{id}' is already registered.", nameof(method));
    }
    public bool UnregisterMethod(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_methods.Remove(id, out _)) return false;
        if (ActiveId == id) Cancel();
        return true;
    }
    internal bool HasTool(string name) => _methods.ContainsKey(name);
    internal bool Start(string name) => Start(name, out _);
    // Publish ownership before callbacks, including when cancellation throws.
    internal bool Start(string name, out long version)
    {
        version = SessionVersion;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_methods.TryGetValue(name, out var method)) return false;
        version = ++SessionVersion;
        CancelCore();
        if (version != SessionVersion || _disposed) return false;
        _active = method.Tool; ActiveId = name; return true;
    }
    internal bool Click(Point point)
    {
        var method = _active;
        if (method == null) return true;
        var version = SessionVersion;
        if (!method.OnClick(point)) return false;
        // A completion subscriber can restart even the same registered tool instance.
        if (version != SessionVersion) return false;
        var scopes = Context.CaptureUncompletedScopes();
        _active = null; ActiveId = null;
        Context.CancelScopes(scopes);
        return version == SessionVersion;
    }
    internal void Move(Point point) { var method = _active; method?.OnMouseMove(point); }
    internal void Cancel() => Cancel(out _);
    internal void Cancel(out long version)
    {
        version = ++SessionVersion;
        CancelCore();
    }
    private void CancelCore()
    {
        var method = _active;
        var scopes = Context.CaptureUncompletedScopes();
        _active = null; ActiveId = null;
        try { method?.Cancel(); }
        finally { Context.CancelScopes(scopes); }
    }
    internal void NotifyFrameCommitted(FrameInfo info) => Context.NotifyFrameCommitted(info);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Cancel(); } finally { Context.Shutdown(); }
    }
}
