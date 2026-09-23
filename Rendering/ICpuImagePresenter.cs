using System.Windows.Media;
using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer.Rendering;

internal interface ICpuImagePresenter : IDisposable
{
    ImageSource Present(DisplayBuffer pixels);
}
