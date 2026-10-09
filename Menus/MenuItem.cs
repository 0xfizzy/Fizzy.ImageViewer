namespace Fizzy.ImageViewer.Menus;

/// <summary>A menu action. Async actions start on the viewer STA and are awaited by the menu binding.</summary>
/// <remarks>The header must contain non-whitespace text. Required delegates are validated at construction;
/// the optional visibility predicate may be null.</remarks>
public sealed class MenuItem : IMenuItem
{
    private readonly Func<ValueTask> _action;
    private readonly Func<bool>? _isVisible;

    public MenuItem(string header, Func<ValueTask> action, Func<bool>? isVisible = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentNullException.ThrowIfNull(action);
        Header = header;
        _action = action;
        _isVisible = isVisible;
    }

    public MenuItem(string header, Action action, Func<bool>? isVisible = null)
        : this(header, Wrap(action), isVisible) { }

    private static Func<ValueTask> Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => { action(); return ValueTask.CompletedTask; };
    }

    public string Header { get; }
    public bool IsVisible => _isVisible?.Invoke() ?? true;
    public ValueTask ExecuteAsync() => _action();
}
