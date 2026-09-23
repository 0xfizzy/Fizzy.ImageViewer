using System;
using System.Windows;

namespace Fizzy.ImageViewer.Menus;

/// <summary>
/// 通用菜单项，支持 lambda 回调。
/// </summary>
public sealed class MenuItem(string header, Action action, Func<bool>? isVisible = null) : IMenuItem
{
    public string Header { get; } = header;
    public bool IsVisible => isVisible?.Invoke() ?? true;
    public void Execute(object sender, RoutedEventArgs e) => action();
}

/// <summary>
/// 可勾选的菜单项，支持 lambda 回调。
/// </summary>
public sealed class CheckableMenuItem(string header, Func<bool> isChecked, Action action, Func<bool>? isVisible = null) : ICheckableMenuItem
{
    public string Header { get; } = header;
    public bool IsVisible => isVisible?.Invoke() ?? true;
    public bool IsChecked => isChecked();
    public void Execute(object sender, RoutedEventArgs e) => action();
}

/// <summary>
/// 分隔符菜单项（单例）。
/// </summary>
public sealed class SeparatorMenuItem : IMenuItem
{
    public static readonly SeparatorMenuItem Instance = new();
    private SeparatorMenuItem() { }

    public string Header => string.Empty;
    public void Execute(object sender, RoutedEventArgs e) { }
}
