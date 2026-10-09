using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Rendering;

/// <summary>The native WPF attachment boundary; all calls run on the owning STA.</summary>
internal interface ID3DImageTarget
{
    ImageSource Image { get; }
    bool IsFrontBufferAvailable { get; }
    event DependencyPropertyChangedEventHandler FrontBufferAvailableChanged;
    void Lock();
    void SetBackBuffer(nint surface);
    void AddDirtyRect(Int32Rect rectangle);
    void Unlock();
}
