namespace Fizzy.ImageViewer.Menus
{
    /// <summary>Menu callbacks run on the viewer STA. A failure while reading display state
    /// cancels that menu opening and is logged; it does not close the viewer.</summary>
    public interface IMenuItem
    {
        /// <summary>Display text read on the viewer STA when the menu opens.</summary>
        string Header { get; }
        /// <summary>Whether the item is included in this opening; evaluated on the viewer STA and defaults to true.</summary>
        bool IsVisible => true;
        /// <summary>Starts on the viewer STA. Already started work may finish after menu or viewer closure.</summary>
        ValueTask ExecuteAsync();
    }
}
