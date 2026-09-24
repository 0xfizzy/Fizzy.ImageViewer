namespace Fizzy.ImageViewer.Menus
{
    public interface IMenuItem
    {
        string Header { get; }
        bool IsVisible => true;
        /// <summary>Starts on the viewer STA. Already started work may finish after menu or viewer closure.</summary>
        ValueTask ExecuteAsync();
    }
}
