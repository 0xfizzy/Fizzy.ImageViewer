namespace Fizzy.ImageViewer.Interaction;

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
