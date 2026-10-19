namespace Fizzy.ImageViewer;

/// <summary>Viewing capabilities and ownership of asynchronous shutdown.</summary>
/// <remarks>Only the owner disposes the viewer. Borrow IViewerCapabilities or individual capability
/// interfaces otherwise. Window operations require IViewerWindow.</remarks>
public interface IViewer : IViewerCapabilities, IAsyncDisposable
{
}
