using Fizzy.ImageViewer.Imaging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public GrayDisplayRange? DisplayRange
    {
        get => _host.Pipeline.DisplayRange;
        set => _host.Pipeline.DisplayRange = value;
    }

    public void FitToViewport()
    {
        InvokeAlive(() => _host.Window.ImageViewport.FitToViewport());
    }
}
