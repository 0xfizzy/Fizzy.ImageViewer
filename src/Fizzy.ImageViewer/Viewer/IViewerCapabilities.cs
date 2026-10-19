using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Hud;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Snapshots;
using Fizzy.ImageViewer.Viewport;

namespace Fizzy.ImageViewer;

/// <summary>Borrowed host-independent viewing capabilities without ownership of shutdown.</summary>
/// <remarks>Implemented by Viewer and ImageViewerControl. Prefer individual capability interfaces when
/// sufficient. Global layers remain mutable shared state. This interface does not provide security
/// isolation or remove WPF dependencies. Window operations require IViewerWindow.</remarks>
public interface IViewerCapabilities : IFrameSink, ICommittedFrameSource, IViewerDisplay,
    ISnapshotSource, IViewerDrawing, IViewerHud, IViewerMeasurements, IViewerMenu, IViewerQueries, IViewerLifetime
{
    /// <summary>Global layer composition, enumeration and content clearing across drawing and measurements.</summary>
    ViewerLayers Layers { get; }
}
