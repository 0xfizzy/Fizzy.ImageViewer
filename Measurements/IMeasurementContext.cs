using Fizzy.ImageViewer.Frames;
using System.Windows;

namespace Fizzy.ImageViewer;

internal interface IMeasurementContext
{
    void VerifyAccess();
    void UpdateAnchor(UIElement shape, Point point);
    void Attach(MeasurementItem item);
    void Detach(MeasurementItem item);
    void NotifyCompleted(MeasurementItem item);
    MeasurementSubscription Register(IFrameMeasurement item);
    void AttachScopeShape(MeasurementScope scope, UIElement shape);
    void DetachScope(MeasurementScope scope, IReadOnlyCollection<UIElement> shapes);
}
