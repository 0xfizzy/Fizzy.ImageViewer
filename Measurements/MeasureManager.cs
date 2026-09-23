using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Tool registry and measurement session execution, independent of input state.</summary>
internal sealed class MeasureManager : IDisposable
{
    internal sealed record Registration(string Id, string DisplayName, IMeasureMethod Method);
    private readonly Dictionary<string, Registration> _methods = new(StringComparer.Ordinal);
    private IMeasureMethod? _active;
    private bool _disposed;
    public Registration[] RegisteredMethods => _methods.Values.ToArray();
    public string? ActiveId { get; private set; }
    public bool HasSelection => _active != null;
    public MeasureContext Context { get; }
    public Task Completion => Context.Completion;
    internal MeasureManager(OverlayLayer output, Func<FrameLease?> acquire, ILogger logger) => Context = new(output, acquire, logger);
    public void RegisterMethod(IMeasureMethod method)
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
    internal bool HasMethod(string name) => _methods.ContainsKey(name);
    internal bool Start(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_methods.TryGetValue(name, out var method)) return false;
        Cancel(); _active = method.Method; ActiveId = name; return true;
    }
    internal bool Click(Point point)
    {
        if (_active == null) return true;
        if (!_active.OnClick(point, Context)) return false;
        _active = null; ActiveId = null; Context.CancelUncompletedScopes(); return true;
    }
    internal void Move(Point point) => _active?.OnMouseMove(point, Context);
    internal void Cancel() { var method = _active; _active = null; ActiveId = null; try { method?.Cancel(Context); } finally { Context.CancelUncompletedScopes(); } }
    internal void NotifyFrameCommitted(FrameInfo info) => Context.NotifyFrameCommitted(info);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Cancel(); } finally { Context.Shutdown(); }
    }
}
