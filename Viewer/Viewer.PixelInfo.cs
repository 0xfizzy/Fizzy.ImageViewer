using Fizzy.ImageViewer.PixelInfo;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private PixelInfoOverlay? _pixelInfoOverlay;
    private void InitializePixelInfoOverlay(ViewerWindow win, Managers.MenuManager menuMgr)
    {
        _pixelInfoOverlay = new PixelInfoOverlay(win.Layer0, win.Layer2, _measureManager!.Context);
        _pixelInfoOverlay.Enable();
        menuMgr.Register(new MenuItems.CheckableMenuItem("Pixel Info", () => _pixelInfoOverlay.IsEnabled,
            () => { if (_pixelInfoOverlay.IsEnabled) _pixelInfoOverlay.Disable(); else _pixelInfoOverlay.Enable(); }));
    }
}
