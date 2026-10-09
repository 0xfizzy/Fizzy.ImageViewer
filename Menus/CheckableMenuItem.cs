namespace Fizzy.ImageViewer.Menus;

/// <summary>A checkable menu action whose state is evaluated on opening.</summary>
/// <remarks>The header must contain non-whitespace text. Required delegates are validated at construction;
/// the optional visibility predicate may be null.</remarks>
public sealed class CheckableMenuItem : ICheckableMenuItem
{
    private readonly Func<bool> _isChecked;
    private readonly Func<ValueTask> _action;
    private readonly Func<bool>? _isVisible;

    public CheckableMenuItem(string header, Func<bool> isChecked, Func<ValueTask> action, Func<bool>? isVisible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(isChecked);
        ArgumentNullException.ThrowIfNull(action);
        Header = header;
        _isChecked = isChecked;
        _action = action;
        _isVisible = isVisible;
    }

    public CheckableMenuItem(string header, Func<bool> isChecked, Action action, Func<bool>? isVisible = null)
        : this(header, isChecked, Wrap(action), isVisible) { }

    private static Func<ValueTask> Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => { action(); return ValueTask.CompletedTask; };
    }

    public string Header { get; }
    public bool IsVisible => _isVisible?.Invoke() ?? true;
    public bool IsChecked => _isChecked();
    public ValueTask ExecuteAsync() => _action();
}
