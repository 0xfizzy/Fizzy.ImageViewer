using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer.Interaction;

/// <summary>Applies interaction effects without owning selection or tool-session state.</summary>
internal interface IInteractionView : IDisposable
{
    bool Capture();
    void EndPan();
    void ShowMeasurementCursor();
    void ShowDragCursor();
    void ShowDefaultCursor();
    void Restore();
    void SetSelection(MeasurementItem? item);
}
