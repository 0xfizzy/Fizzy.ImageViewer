namespace Fizzy.ImageViewer.Measurements;

/// <summary>Measurement tools, interaction, style and notifications on the viewer STA.</summary>
public interface IViewerMeasurements
{
    /// <summary>The entire measurement layer. Clearing it cancels interaction and removes all measurements.
    /// Cleanup attempts every item and propagates failures to the caller.</summary>
    MeasurementLayer Measurements { get; }

    /// <summary>Style for future measurements. Brushes are copied and frozen before UI dispatch.</summary>
    MeasurementStyle MeasurementStyle { get; set; }

    /// <summary>
    /// Activates a registered tool with a fresh session that can create zero or more measurements.
    /// </summary>
    void ActivateMeasurementTool(string toolId);

    /// <summary>
    /// Ends creation or editing, preserving completed measurements and edits already applied.
    /// </summary>
    /// <remarks>Session and unfinished-measurement cleanup are all attempted. Failures propagate
    /// to the caller, aggregated when multiple stages fail; cleanup is not retried.</remarks>
    void EndInteraction();

    /// <summary>Completed measurements only; raised on the viewer STA, excluding previews.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementCompleted;

    /// <summary>Removal of completed measurements; raised on the viewer STA, including closure.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementRemoved;

    /// <summary>Geometry and query snapshots plus a live editable handle after editing, publication or invalidation.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementChanged;

    /// <summary>
    /// Registers a custom tool; CreateSession creates a fresh session for each activation. Create previews through IMeasurementToolContext.CreateMeasurement.
    /// Complete retains the result; the viewer owns cancellation, removal, clear and shutdown cleanup.
    /// Schedulers, arbitrary geometry implementations and per-frame query registration are not public extension points.
    /// </summary>
    /// <remarks>Id and DisplayName are read once on the viewer STA and captured for the registration.
    /// If a metadata getter initiates viewer shutdown, registration is rejected with ObjectDisposedException.</remarks>
    /// <returns>A thread-safe, idempotent handle revoking only this registration. Completed measurements survive.</returns>
    IDisposable RegisterMeasurementTool(IMeasurementTool tool);

    /// <summary>Removes the viewer-wide registration for this ID, including an active session using it.</summary>
    bool UnregisterMeasurementTool(string toolId);
}
