using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Rendering;

internal sealed class D3DImageTarget : ID3DImageTarget
{
    private readonly D3DImage _image = new();
    public ImageSource Image => _image;
    public bool IsFrontBufferAvailable => _image.IsFrontBufferAvailable;
    public event DependencyPropertyChangedEventHandler FrontBufferAvailableChanged
    {
        add => _image.IsFrontBufferAvailableChanged += value;
        remove => _image.IsFrontBufferAvailableChanged -= value;
    }
    public void Lock() => _image.Lock();
    public void SetBackBuffer(nint surface) => _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface);
    public void AddDirtyRect(Int32Rect rectangle) => _image.AddDirtyRect(rectangle);
    public void Unlock() => _image.Unlock();
}
