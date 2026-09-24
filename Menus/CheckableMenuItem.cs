namespace Fizzy.ImageViewer.Menus;

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
