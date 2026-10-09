using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>STA-owned presenter. Surface leases outlive both WPF attachment and front-buffer loss.</summary>
internal sealed class D3DImagePresenter : IDisposable
{
    // Failed native detach cannot prove that WPF stopped using either surface.
    // Root the target and leases for the process lifetime; never release or reuse
    // these resources on the strength of a failed close, or retry destructive cleanup.
    private static readonly ConcurrentBag<D3DImagePresenter> Quarantined = [];
    private readonly ID3DImageTarget _target;
    private FrameLease? _frame;
    private FrameLease? _uncertainFrame;
    private bool _faulted;
    private bool _disposed;

    public D3DImagePresenter() : this(new D3DImageTarget()) { }
    internal D3DImagePresenter(ID3DImageTarget target)
    {
        _target = target;
        _target.FrontBufferAvailableChanged += FrontBufferChanged;
    }

    public ImageSource Present(FrameLease frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted) throw new InvalidOperationException("The D3D presenter has failed and cannot accept another surface.");
        var next = frame.Acquire();
        try { Attach(next); }
        catch
        {
            _faulted = true;
            _uncertainFrame = next;
            throw;
        }
        var previous = _frame;
        _frame = next;
        previous?.Dispose();
        return _target.Image;
    }

    private void Attach(FrameLease frame) => WithLock(() =>
    {
        _target.SetBackBuffer(frame.D3D9Surface);
        if (_target.IsFrontBufferAvailable)
            _target.AddDirtyRect(new Int32Rect(0, 0, frame.Descriptor.Width, frame.Descriptor.Height));
    });

    private void WithLock(Action action)
    {
        _target.Lock();
        Exception? failure = null;
        try { action(); } catch (Exception ex) { failure = ex; }
        try { _target.Unlock(); }
        catch (Exception ex)
        {
            if (failure != null) throw new AggregateException(failure, ex);
            throw;
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void FrontBufferChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed || _faulted || !_target.IsFrontBufferAvailable || _frame == null) return;
        try { Attach(_frame); }
        catch { _faulted = true; throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _target.FrontBufferAvailableChanged -= FrontBufferChanged;
        try { WithLock(() => _target.SetBackBuffer(IntPtr.Zero)); }
        catch
        {
            Quarantined.Add(this);
            throw;
        }
        var frame = _frame;
        var uncertain = _uncertainFrame;
        _frame = _uncertainFrame = null;
        List<Exception> errors = [];
        try { frame?.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        try { uncertain?.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("D3D surface release failed.", errors);
    }
}
