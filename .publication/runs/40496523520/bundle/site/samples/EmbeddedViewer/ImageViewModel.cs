using Fizzy.ImageViewer.Frames;

namespace EmbeddedViewer;

/// <summary>The window creates and disposes this model on its UI thread.</summary>
public sealed class ImageViewModel : IDisposable
{
    public ImageBindingSource Image { get; } = new();
    private int _imageNumber;

    public async Task LoadNextAsync()
    {
        var number = ++_imageNumber;
        // Prepare pixels off the UI thread. The await resumes on the host dispatcher.
        using var frame = await Task.Run(() =>
        {
            const int width = 640, height = 480;
            var pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y * width + x] = (byte)((x / 32 + y / 32 + number) % 2 * 200 + 30);
            // Transfer the buffer into immutable storage. Never write it again.
            return ImageFrame.TakeOwnership(new(width, height, width, FramePixelFormat.Gray8), pixels, static () => { });
        });
        Image.SetFrame(frame); // Retains its own reference; does not consume frame.
        // Disposing our frame is safe even if WPF has not yet read the Image property.
    }

    public void Dispose() => Image.Dispose();
}
