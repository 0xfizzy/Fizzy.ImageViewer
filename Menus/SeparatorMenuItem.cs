namespace Fizzy.ImageViewer.Menus;

/// <summary>A separator normalized by the menu renderer.</summary>
public sealed class SeparatorMenuItem : IMenuItem
{
    /// <summary>Shared stateless separator; leading, repeated and trailing separators are omitted by the menu renderer.</summary>
    public static readonly SeparatorMenuItem Instance = new();
    private SeparatorMenuItem() { }
    /// <summary>Empty header; the menu renderer displays a separator instead of text.</summary>
    public string Header => string.Empty;
    /// <summary>Completes immediately without performing an action.</summary>
    public ValueTask ExecuteAsync() => ValueTask.CompletedTask;
}
