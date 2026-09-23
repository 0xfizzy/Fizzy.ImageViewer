using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Tool registry and measurement session execution, independent of input state.</summary>
internal sealed class MeasurementManager : IDisposable
{
    internal sealed record Registration(string Id, string DisplayName, IMeasurementToolHandler Tool);
    private readonly Dictionary<string, Registration> _tools = new(StringComparer.Ordinal);
    private IMeasurementToolHandler? _active;
    private bool _disposed;
    internal long SessionVersion { get; private set; }
    public Registration[] RegisteredTools => _tools.Values.ToArray();
    public string? ActiveId { get; private set; }
    public bool IsMeasuring => _active != null;
    public MeasurementContext Context { get; }
    internal MeasurementManager(OverlayLayer output, Func<FrameLease?> acquire, PixelQueryScheduler scheduler, ILogger logger) => Context = new(output, acquire, scheduler, logger);
    public void RegisterTool(IMeasurementTool tool)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tool);
        RegisterTool(new CustomMeasurementTool(tool, Context));
    }
    internal void RegisterTool(IMeasurementToolHandler tool)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tool);
        var id = tool.Id; var name = tool.DisplayName;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_tools.TryAdd(id, new(id, name, tool))) throw new ArgumentException($"Measurement tool '{id}' is already registered.", nameof(tool));
    }
    public bool UnregisterTool(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tools.Remove(id, out _)) return false;
        if (ActiveId == id) Cancel();
        return true;
    }
    internal bool HasTool(string name) => _tools.ContainsKey(name);
    internal bool Start(string name) => Start(name, out _);
    // Publish ownership before callbacks, including when cancellation throws.
    internal bool Start(string name, out long version)
    {
        version = SessionVersion;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tools.TryGetValue(name, out var tool)) return false;
        version = ++SessionVersion;
        CancelCore();
        if (version != SessionVersion || _disposed) return false;
        _active = tool.Tool; ActiveId = name; return true;
    }
    internal bool Click(Point point)
    {
        var tool = _active;
        if (tool == null) return true;
        var version = SessionVersion;
        if (!tool.OnClick(point)) return false;
        // A completion subscriber can restart even the same registered tool instance.
        if (version != SessionVersion) return false;
        var scopes = Context.CaptureUncompletedScopes();
        _active = null; ActiveId = null;
        Context.CancelScopes(scopes);
        return version == SessionVersion;
    }
    internal void Move(Point point) { var tool = _active; tool?.OnMouseMove(point); }
    internal void Cancel() => Cancel(out _);
    internal void Cancel(out long version)
    {
        version = ++SessionVersion;
        CancelCore();
    }
    private void CancelCore()
    {
        var tool = _active;
        var scopes = Context.CaptureUncompletedScopes();
        _active = null; ActiveId = null;
        try { tool?.Cancel(); }
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
