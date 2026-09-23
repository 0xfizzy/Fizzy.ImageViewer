using System.Windows;

namespace Fizzy.ImageViewer.Interfaces
{
    public interface IMenuItem
    {
        string Header { get; }
        bool IsVisible => true;
        void Execute(object sender, RoutedEventArgs e);
    }
}
