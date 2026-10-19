namespace Fizzy.ImageViewer;

/// <summary>Read-only observation of a viewer's terminal admission boundary.</summary>
public interface IViewerLifetime
{
    /// <summary>Gets the shared task that completes successfully when the viewer stops accepting new work.</summary>
    /// <remarks>Reading or awaiting this task does not initiate shutdown. It remains completed for late
    /// observers and never faults or cancels, including when subsequent cleanup fails. Completion does
    /// not mean accepted work has drained, resources have been released, or the standalone STA has exited.
    /// The owner must still await IAsyncDisposable.DisposeAsync. Temporary unloading does not complete
    /// this task. Continuations are not invoked inline by the signal; no UI-thread delivery is promised.
    /// Concurrent operations must still handle shutdown rejection and honor frame ownership transfer.</remarks>
    Task WhenStopping { get; }
}
