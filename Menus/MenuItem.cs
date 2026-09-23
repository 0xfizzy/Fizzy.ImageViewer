using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Windows;

namespace Fizzy.ImageViewer.Menus;

/// <summary>
/// 通用菜单项，支持 lambda 回调。
/// </summary>
public sealed class MenuItem(string header, Action action, MenuItemType type = MenuItemType.General) : IMenuItem
{
    public string Header { get; } = header;
    public MenuItemType Type { get; } = type;
    public void Execute(object sender, RoutedEventArgs e) => action();
}

/// <summary>
/// 可勾选的菜单项，支持 lambda 回调。
/// </summary>
public sealed class CheckableMenuItem(string header, Func<bool> isChecked, Action action, MenuItemType type = MenuItemType.General) : ICheckableMenuItem
{
    public string Header { get; } = header;
    public MenuItemType Type { get; } = type;
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
    public MenuItemType Type => MenuItemType.Separator;
    public void Execute(object sender, RoutedEventArgs e) { }
}
