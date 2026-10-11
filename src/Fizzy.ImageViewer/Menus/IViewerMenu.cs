namespace Fizzy.ImageViewer.Menus;

/// <summary>Registers borrowed menu objects and returns independently revocable registration handles.</summary>
public interface IViewerMenu
{
    /// <summary>
    /// Registers a borrowed menu item. Disposing the returned handle revokes this registration without disposing the menu object.
    /// The handle can be disposed repeatedly from any thread, including after viewer closure.
    /// </summary>
    IDisposable RegisterMenuItem(IMenuItem menuItem);
}
