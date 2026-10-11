namespace Fizzy.ImageViewer.Snapshots;

/// <summary>Pixel representation captured independently of the viewer lifetime.</summary>
public enum SnapshotKind
{
    /// <summary>Original pixel format and values, without display mapping; saving requires TIFF.</summary>
    Raw,
    /// <summary>Committed display mapping without overlays, HUD or viewport scaling.</summary>
    Display
}
