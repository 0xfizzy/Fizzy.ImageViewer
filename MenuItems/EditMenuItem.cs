using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Windows;

namespace Fizzy.ImageViewer.MenuItems;

/// <summary>
/// Menu item for entering shape edit mode.
/// </summary>
public class EditMenuItem : IMenuItem
{
    private readonly Action _executeAction;

    public string Header => "Edit";
    public MenuItemType Type => MenuItemType.SelectionAction;

    public EditMenuItem(Action executeAction)
    {
        _executeAction = executeAction;
    }

    public void Execute(object sender, RoutedEventArgs e)
    {
        _executeAction?.Invoke();
    }
}
