namespace Fizzy.ImageViewer.Menus;

/// <summary>A checkable menu action whose state is evaluated on opening.</summary>
/// <remarks>The header must contain non-whitespace text. Required delegates are validated at construction;
/// the optional visibility predicate may be null.</remarks>
public sealed class CheckableMenuItem : ICheckableMenuItem
{
    private readonly Func<bool> _isChecked;
    private readonly Func<ValueTask> _action;
    private readonly Func<bool>? _isVisible;

    /// <summary>Creates a menu item with a asynchronous action.</summary>
    /// <param name="header">Nonblank display text, preserved as supplied.</param>
    /// <param name="isChecked">Required checked-state getter, evaluated when the menu opens.</param>
    /// <param name="action">Required action invoked on the viewer STA.</param>
    /// <param name="isVisible">Optional visibility predicate, evaluated on opening; null means visible.</param>
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

    /// <summary>Creates a menu item with a synchronous action.</summary>
    /// <param name="header">Nonblank display text, preserved as supplied.</param>
    /// <param name="isChecked">Required checked-state getter, evaluated when the menu opens.</param>
    /// <param name="action">Required action invoked on the viewer STA.</param>
    /// <param name="isVisible">Optional visibility predicate, evaluated on opening; null means visible.</param>
    public CheckableMenuItem(string header, Func<bool> isChecked, Action action, Func<bool>? isVisible = null)
        : this(header, isChecked, Wrap(action), isVisible) { }

    private static Func<ValueTask> Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => { action(); return ValueTask.CompletedTask; };
    }

    /// <inheritdoc/>
    public string Header { get; }
    /// <inheritdoc/>
    public bool IsVisible => _isVisible?.Invoke() ?? true;
    /// <inheritdoc/>
    public bool IsChecked => _isChecked();
    /// <inheritdoc/>
    public ValueTask ExecuteAsync() => _action();
}
