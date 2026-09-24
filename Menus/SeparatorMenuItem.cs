namespace Fizzy.ImageViewer.Menus;

/// <summary>A separator normalized by the menu renderer.</summary>
public sealed class SeparatorMenuItem : IMenuItem
{
    public static readonly SeparatorMenuItem Instance = new();
    private SeparatorMenuItem() { }
    public string Header => string.Empty;
    public ValueTask ExecuteAsync() => ValueTask.CompletedTask;
}
