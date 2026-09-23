using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.PixelInfo;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    private PixelInfoOverlay? _pixelInfoOverlay;
    private void InitializePixelInfoOverlay(ViewerWindow win, Menus.MenuManager menuMgr)
    {
        _pixelInfoOverlay = new PixelInfoOverlay(win.ImageLayer, win.HudLayer, _queryScheduler);
        _pixelInfoOverlay.Enable();
        menuMgr.Register(new Menus.CheckableMenuItem("Pixel Info", () => _pixelInfoOverlay.IsEnabled,
            () => { if (_pixelInfoOverlay.IsEnabled) _pixelInfoOverlay.Disable(); else _pixelInfoOverlay.Enable(); }));
    }
}
