namespace Fizzy.ImageViewer.Interaction;

internal interface IMouseCapture
{
    bool IsCaptured { get; }
    bool Capture();
    void Release();
}
