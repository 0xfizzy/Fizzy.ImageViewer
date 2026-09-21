using System.Windows;
using System.Windows.Interop;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>STA-owned presenter. Surface leases outlive both WPF attachment and front-buffer loss.</summary>
internal sealed class D3DImagePresenter : IDisposable
{
    private readonly D3DImage _image = new();
    private FrameLease? _frame;
    public D3DImagePresenter() => _image.IsFrontBufferAvailableChanged += FrontBufferChanged;

    public D3DImage Present(FrameLease frame)
    {
        var next = frame.Acquire();
        try { Attach(next); }
        catch { next.Dispose(); throw; }
        var previous = _frame;
        _frame = next;
        previous?.Dispose();
        return _image;
    }

    private void Attach(FrameLease frame)
    {
        _image.Lock();
        try
        {
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, frame.D3D9Surface);
            if (_image.IsFrontBufferAvailable)
                _image.AddDirtyRect(new Int32Rect(0, 0, frame.Descriptor.Width, frame.Descriptor.Height));
        }
        finally { _image.Unlock(); }
    }

    private void FrontBufferChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_image.IsFrontBufferAvailable && _frame != null) Attach(_frame);
    }

    public void Dispose()
    {
        _image.IsFrontBufferAvailableChanged -= FrontBufferChanged;
        _image.Lock();
        try { _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); }
        finally { _image.Unlock(); }
        _frame?.Dispose();
        _frame = null;
    }
}
