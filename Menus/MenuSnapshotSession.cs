using Fizzy.ImageViewer.Rendering;
using Fizzy.ImageViewer.Frames;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Menus;

/// <summary>Owns a menu target until input drains; callers acquire independent save targets.</summary>
internal sealed class MenuSnapshotSession(FramePipeline pipeline, ViewerLifetime lifetime,
    Dispatcher dispatcher, ILogger logger) : IDisposable
{
    private Target? _target;
    private long _generation;
    private bool _open, _disposed;
    private int _saving;

    internal sealed class Target(CommittedViewLease view, PixelRegion? region) : IDisposable
    {
        internal CommittedViewLease View { get; } = view;
        internal PixelRegion? Region { get; } = region;
        internal Target Acquire(bool includeRegion) => new(View.Acquire(), includeRegion ? Region : null);
        public void Dispose() => View.Dispose();
    }

    internal bool HasRegion { get { lock (lifetime.Gate) return _target?.Region is { IsEmpty: false }; } }
    internal bool TryBeginSave() => Interlocked.CompareExchange(ref _saving, 1, 0) == 0;
    internal void EndSave() => Interlocked.Exchange(ref _saving, 0);

    internal void Open(Func<FrameDescriptor, PixelRegion?>? getRegion = null)
    {
        dispatcher.VerifyAccess();
        var view = pipeline.FreezeAndAcquire();
        Target? next;
        try { next = view == null ? null : new(view, getRegion?.Invoke(view.Frame.Descriptor)); }
        catch
        {
            Release(view);
            pipeline.Resume();
            throw;
        }
        Target? previous;
        lock (lifetime.Gate)
        {
            previous = _target;
            _target = next;
            _generation++;
            _open = true;
        }
        Release(previous);
    }

    internal void Close()
    {
        dispatcher.VerifyAccess();
        pipeline.Resume();
        long generation;
        lock (lifetime.Gate)
        {
            _open = false;
            generation = _generation;
        }
        // WPF may deliver Closed before Click. A subsequent Open invalidates this cleanup.
        dispatcher.BeginInvoke(() =>
        {
            Target? previous;
            lock (lifetime.Gate)
            {
                if (_disposed || _open || generation != _generation) return;
                previous = _target;
                _target = null;
            }
            Release(previous);
        }, DispatcherPriority.ContextIdle);
    }

    internal Target? AcquireTarget(bool region = false)
    {
        lock (lifetime.Gate)
        {
            lifetime.ThrowIfStopping();
            if (region && _target?.Region is not { IsEmpty: false }) return null;
            if (_target != null) return _target.Acquire(region);
            var view = pipeline.AcquireCommittedView();
            return view == null ? null : new(view, null);
        }
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        Target? previous;
        lock (lifetime.Gate)
        {
            if (_disposed) return;
            _disposed = true;
            _open = false;
            _generation++;
            previous = _target;
            _target = null;
        }
        Release(previous);
    }

    private void Release(IDisposable? target)
    {
        try { target?.Dispose(); }
        catch (Exception ex) { logger.LogWarning(ex, "Menu target release failed"); }
    }
}
