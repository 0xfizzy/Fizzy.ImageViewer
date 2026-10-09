namespace Fizzy.ImageViewer.Menus
{
    /// <summary>Menu callbacks run on the viewer STA. A failure while reading display state
    /// cancels that menu opening and is logged; it does not close the viewer.</summary>
    public interface IMenuItem
    {
        string Header { get; }
        bool IsVisible => true;
        /// <summary>Starts on the viewer STA. Already started work may finish after menu or viewer closure.</summary>
        ValueTask ExecuteAsync();
    }
}
