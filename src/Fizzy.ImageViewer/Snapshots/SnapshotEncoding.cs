namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Snapshot file encoding. Raw snapshots require TIFF; display snapshots support all listed encodings.</summary>
public enum SnapshotEncoding
{
    /// <summary>Uncompressed Classic TIFF preserving original numeric precision for raw snapshots.</summary>
    Tiff,
    /// <summary>PNG display image.</summary>
    Png,
    /// <summary>Lossy JPEG display image; transparency is composited onto black.</summary>
    Jpeg,
    /// <summary>BMP display image.</summary>
    Bmp
}
