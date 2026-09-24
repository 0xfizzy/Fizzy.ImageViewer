using System.Windows;

namespace Fizzy.ImageViewer.Interaction;

internal interface IMouseCapture
{
    bool IsCaptured { get; }
    bool Capture();
    void Release();
}

internal sealed class ElementMouseCapture(UIElement element) : IMouseCapture
{
    public bool IsCaptured => element.IsMouseCaptured;
    public bool Capture() => element.CaptureMouse();
    public void Release() => element.ReleaseMouseCapture();
}

/// <summary>Shared capture admission and termination for viewport and measurement drags.</summary>
internal sealed class MouseCaptureSession(IMouseCapture capture)
{
    internal bool IsActive { get; private set; }
    internal bool Begin()
    {
        End();
        IsActive = capture.Capture() && capture.IsCaptured;
        return IsActive;
    }
    internal bool Lost()
    {
        bool active = IsActive;
        IsActive = false;
        return active;
    }
    internal void End()
    {
        bool active = Lost();
        if (active && capture.IsCaptured) capture.Release();
    }
}
