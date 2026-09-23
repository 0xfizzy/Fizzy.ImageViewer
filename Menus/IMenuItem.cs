using System.Windows;

namespace Fizzy.ImageViewer.Menus
{
    public interface IMenuItem
    {
        string Header { get; }
        bool IsVisible => true;
        void Execute(object sender, RoutedEventArgs e);
    }
}
