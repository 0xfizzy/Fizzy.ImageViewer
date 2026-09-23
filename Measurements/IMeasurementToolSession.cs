using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>One activation of a measurement tool. Callbacks run on the viewer STA.</summary>
public interface IMeasurementToolSession
{
    /// <summary>Returns true to end input. Only measurements explicitly completed are retained.</summary>
    bool OnClick(Point point);
    void OnMouseMove(Point point);
    /// <summary>Called once when interrupted, including an activation superseded during its factory.
    /// The creation context has already ended; unfinished measurements are released by the framework.</summary>
    void Cancel();
}
