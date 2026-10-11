using System.Windows;

namespace Fizzy.ImageViewer.Interaction;

internal sealed class ElementMouseCapture(UIElement element) : IMouseCapture
{
    public bool IsCaptured => element.IsMouseCaptured;
    public bool Capture() => element.CaptureMouse();
    public void Release() => element.ReleaseMouseCapture();
}
