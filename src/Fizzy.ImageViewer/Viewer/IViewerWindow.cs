namespace Fizzy.ImageViewer;

/// <summary>Borrowed window control. Operations dispatch to the viewer STA and reject calls once shutdown starts.</summary>
public interface IViewerWindow
{
    /// <summary>
    /// Shows, restores and activates the window on the viewer STA. Throws ObjectDisposedException after shutdown begins.
    /// </summary>
    void Show();

    /// <summary>Hides the window on its STA, retaining frames and drawings. Throws ObjectDisposedException after shutdown begins.</summary>
    void Hide();

    /// <summary>Minimizes the window on its STA. Throws ObjectDisposedException after shutdown begins.</summary>
    void Minimize();

    /// <summary>Reads visibility on the viewer STA; a shown minimized window remains visible. Throws ObjectDisposedException after shutdown begins.</summary>
    bool IsVisible { get; }

    /// <summary>Reads minimized state on the viewer STA. Throws ObjectDisposedException after shutdown begins.</summary>
    bool IsMinimized { get; }

    /// <summary>
    /// Whether the user may close the window using its close button.
    /// </summary>
    bool CanUserClose { get; set; }

    /// <summary>Window closure notification on the viewer STA. Owners must still await IViewer.DisposeAsync
    /// to drain background work and wait for actual STA termination. Runs after queued measurement notifications.</summary>
    /// <remarks>Sender is the public object that owns this event. Adding a subscriber rejects a stopping
    /// viewer with ObjectDisposedException; removing one is always safe. Duplicate subscriptions follow
    /// delegate semantics. Unsubscription does not retract captured notifications; subscriptions added
    /// during delivery apply only to later notifications. Synchronous subscriber failures are logged and
    /// isolated. Async-void work is not awaited; subscribers own its failures.</remarks>
    /// <seealso cref="IViewerLifetime.WhenStopping"/>
    event EventHandler? Closed;

    /// <summary>
    /// Window title.
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Window width in device-independent units.
    /// </summary>
    double Width { get; set; }

    /// <summary>
    /// Window height in device-independent units.
    /// </summary>
    double Height { get; set; }

    /// <summary>
    /// Left window position in device-independent units.
    /// </summary>
    double Left { get; set; }

    /// <summary>
    /// Top window position in device-independent units.
    /// </summary>
    double Top { get; set; }

    /// <summary>
    /// Whether borderless mode is enabled.
    /// </summary>
    bool Borderless { get; set; }
}
