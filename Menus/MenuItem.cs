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

/// <summary>A checkable menu action whose state is evaluated on opening.</summary>
public sealed class CheckableMenuItem(string header, Func<bool> isChecked, Func<ValueTask> action, Func<bool>? isVisible = null) : ICheckableMenuItem
{
    public CheckableMenuItem(string header, Func<bool> isChecked, Action action, Func<bool>? isVisible = null)
        : this(header, isChecked, () => { action(); return ValueTask.CompletedTask; }, isVisible) { }
    public string Header { get; } = header;
    public bool IsVisible => isVisible?.Invoke() ?? true;
    public bool IsChecked => isChecked();
    public ValueTask ExecuteAsync() => action();
}

/// <summary>A separator normalized by the menu renderer.</summary>
public sealed class SeparatorMenuItem : IMenuItem
{
    public static readonly SeparatorMenuItem Instance = new();
    private SeparatorMenuItem() { }
    public string Header => string.Empty;
    public ValueTask ExecuteAsync() => ValueTask.CompletedTask;
}
