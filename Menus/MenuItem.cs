namespace Fizzy.ImageViewer.Menus;

/// <summary>A menu action. Async actions start on the viewer STA and are awaited by the menu binding.</summary>
public sealed class MenuItem(string header, Func<ValueTask> action, Func<bool>? isVisible = null) : IMenuItem
{
    public MenuItem(string header, Action action, Func<bool>? isVisible = null)
        : this(header, () => { action(); return ValueTask.CompletedTask; }, isVisible) { }
    public string Header { get; } = header;
    public bool IsVisible => isVisible?.Invoke() ?? true;
    public ValueTask ExecuteAsync() => action();
}
