using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fizzy.ImageViewer.Imaging;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Rendering;

internal sealed class WriteableBitmapPresenter : IImagePresenter
{
    private WriteableBitmap? _front, _back;
    public ImageSource Present(DisplayBuffer pixels)
    {
        var format = pixels.Format switch
        {
            FramePixelFormat.Gray8 => PixelFormats.Gray8,
            FramePixelFormat.Rgb24 => PixelFormats.Rgb24,
            FramePixelFormat.Bgr24 => PixelFormats.Bgr24,
            FramePixelFormat.Bgr32 => PixelFormats.Bgr32,
            FramePixelFormat.Bgra32 => PixelFormats.Bgra32,
            FramePixelFormat.Pbgra32 => PixelFormats.Pbgra32,
            _ => throw new NotSupportedException("Display pixels must be converted first.")
        };
        if (_back == null || _back.PixelWidth != pixels.Width || _back.PixelHeight != pixels.Height || _back.Format != format)
            _back = new(pixels.Width, pixels.Height, 96, 96, format, null);
        _back.WritePixels(new Int32Rect(0, 0, pixels.Width, pixels.Height), pixels.Bytes, pixels.Stride, 0);
        (_front, _back) = (_back, _front);
        return _front;
    }
    public void Dispose() { _front = _back = null; }
}
