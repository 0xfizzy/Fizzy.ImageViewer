using System.Windows.Input;

namespace Fizzy.ImageViewer;

/// <summary>Routed commands handled by ImageViewerControl, without an MVVM framework dependency.</summary>
/// <remarks>Use an explicit CommandTarget for toolbars and menus controlling a specific viewer;
/// otherwise WPF routes through keyboard focus. No global key gestures are installed.
/// Commands are unavailable while unloaded, hidden, disabled, minimized or stopping.
/// Execution rechecks availability; unavailable commands do nothing. Tool callback exceptions propagate
/// to the WPF command caller. CanExecute never creates a tool session.</remarks>
public static class ImageViewerCommands
{
    /// <summary>Fits the displayed image to a nonempty viewport. No parameter is required.</summary>
    public static RoutedCommand FitToViewport { get; } = new(nameof(FitToViewport), typeof(ImageViewerCommands));

    /// <summary>Activates the registered tool identified by a string parameter, including custom tools.</summary>
    /// <remarks>Null, blank, non-string and unknown IDs are unavailable, as are hidden, input-disabled
    /// or clearing measurement layers. Registration metadata is checked without invoking user tool code.</remarks>
    public static RoutedCommand ActivateMeasurementTool { get; } = new(nameof(ActivateMeasurementTool), typeof(ImageViewerCommands));

    /// <summary>Ends an active measurement creation or edit, preserving completed measurements and applied edits.</summary>
    /// <remarks>Unavailable when no creation or edit is active. No parameter is required.</remarks>
    public static RoutedCommand EndInteraction { get; } = new(nameof(EndInteraction), typeof(ImageViewerCommands));
}
