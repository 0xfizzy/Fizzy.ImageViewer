using Fizzy.ImageViewer.Imaging.Queries;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Internal measurement resource protocol: registers items and scopes, associates
/// visuals, owns query subscriptions, and reports completion.</summary>
/// <remarks>All operations, including disposal of returned subscriptions, run on the owning
/// viewer's UI/STA thread. This protocol does not marshal calls; VerifyAccess checks the caller.
/// The scheduler samples off the UI thread and publishes results back on it.
/// External tools use IMeasurementToolContext instead.</remarks>
internal interface IMeasurementContext
{
    Drawing.ShapeStyle Style { get; }
    void VerifyAccess();
    void UpdateAnchor(UIElement shape, Point point);
    void Attach(MeasurementItem item);
    void Detach(MeasurementItem item);
    void NotifyCompleted(MeasurementItem item);
    QuerySubscription Register(IFrameQueryClient item);
    void AttachScopeShape(MeasurementScope scope, UIElement shape);
    void DetachScope(MeasurementScope scope, IReadOnlyCollection<UIElement> shapes);
}
