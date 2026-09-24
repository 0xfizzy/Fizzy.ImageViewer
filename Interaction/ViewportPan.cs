using System.Windows;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>A viewport drag in screen coordinates, independent of image zoom.</summary>
internal sealed class ViewportPan(IMouseCapture capture)
{
    private readonly MouseCaptureSession _capture = new(capture);
    private Point _previous;
    internal bool IsActive => _capture.IsActive;
    internal bool Begin(Point point)
    {
        if (!_capture.Begin()) return false;
        _previous = point;
        return true;
    }
    internal Vector Move(Point point)
    {
        if (!IsActive) return default;
        var delta = point - _previous;
        _previous = point;
        return delta;
    }
    internal void End() => _capture.End();
    internal void LostCapture() => _capture.Lost();
}
