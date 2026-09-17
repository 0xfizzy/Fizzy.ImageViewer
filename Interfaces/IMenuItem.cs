using Fizzy.ImageViewer.Enums;
using System.Windows;

namespace Fizzy.ImageViewer.Interfaces
{
    public interface IMenuItem
    {
        string Header { get; }
        MenuItemType Type { get; }
        void Execute(object sender, RoutedEventArgs e);
    }
}
