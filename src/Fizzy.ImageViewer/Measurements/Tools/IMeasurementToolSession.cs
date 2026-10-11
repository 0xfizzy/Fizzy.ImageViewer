using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>One activation of a measurement tool. Callbacks run on the viewer STA.
/// Dispose is called exactly once after completion, interruption or callback failure.
/// Cancellation notification is separate from unconditional session-resource cleanup.</summary>
public interface IMeasurementToolSession : IDisposable
{
    /// <summary>Return Finish to end input. Only measurements explicitly completed are retained.</summary>
    MeasurementClickResult OnClick(Point point);
    /// <summary>Updates the active preview for a pointer position in source-image pixels, on the viewer STA.</summary>
    void OnMouseMove(Point point);
    /// <summary>Called once when interrupted, including an activation superseded during its factory.
    /// The creation context has already ended; unfinished measurements are released by the framework.</summary>
    void Cancel();
}
