using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>Applies interaction effects without owning selection or tool-session state.</summary>
internal interface IInteractionView : IDisposable
{
    double ImageScale { get; }
    void SetLayerInputSuppressed(bool suppressed);
    bool Capture();
    void EndPan();
    void ShowMeasurementCursor();
    void ShowDragCursor();
    void ShowDefaultCursor();
    void Restore();
    void SetSelection(MeasurementItem? item);
}
