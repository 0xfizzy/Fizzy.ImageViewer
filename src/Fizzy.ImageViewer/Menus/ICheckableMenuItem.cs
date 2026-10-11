namespace Fizzy.ImageViewer.Menus;

/// <summary>
/// A menu item with checked state.
/// </summary>
public interface ICheckableMenuItem : IMenuItem
{
    /// <summary>
    /// Whether the item is currently checked.
    /// </summary>
    bool IsChecked { get; }
}
