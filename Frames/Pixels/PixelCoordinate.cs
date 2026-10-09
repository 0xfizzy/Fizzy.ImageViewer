namespace Fizzy.ImageViewer.Frames;

/// <summary>Zero-based integer location in an image.</summary>
/// <param name="X">Column index from the left edge.</param>
/// <param name="Y">Row index from the top edge.</param>
public readonly record struct PixelCoordinate(int X, int Y);
