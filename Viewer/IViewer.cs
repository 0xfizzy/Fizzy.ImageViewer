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

/// <summary>Complete viewer capabilities and ownership of asynchronous shutdown.</summary>
/// <remarks>Borrow individual capability interfaces when the caller does not own the viewer.</remarks>
public interface IViewer : IFrameSink, ICommittedFrameSource, IViewerWindow, IViewerDisplay,
    ISnapshotSource, IViewerDrawing, IViewerHud, IViewerMeasurements, IViewerMenu, IViewerQueries, IAsyncDisposable
{
    /// <summary>Global layer composition, enumeration and content clearing across drawing and measurements.</summary>
    ViewerLayers Layers { get; }
}
