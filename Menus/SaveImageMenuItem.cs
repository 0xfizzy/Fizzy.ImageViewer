using Microsoft.Win32;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Snapshots;
using System.Windows;

namespace Fizzy.ImageViewer.Menus;

public sealed class SaveImageMenuItem(Viewer viewer, bool raw, bool region = false) : IMenuItem
{
    public bool IsVisible => !region || viewer.HasMenuRegion;
    public string Header => region ? (raw ? "Export Region Raw TIFF..." : "Save Region Display Image As...") : raw ? "Export Raw TIFF..." : "Save Display Image As...";
    public MenuItemType Type => region ? MenuItemType.SelectionAction : MenuItemType.General;
    public async void Execute(object sender, RoutedEventArgs e)
    {
        if (!viewer.TryBeginSave()) return;
        try
        {
            using var target = (region ? viewer.AcquireMenuRegionSnapshot() : viewer.AcquireMenuSnapshot());
            if (target == null) { MessageBox.Show("No image to save."); return; }
            var dialog = new SaveFileDialog
            {
                Filter = raw ? "TIFF Image|*.tif" : "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                FileName = region ? "Region" : "Image"
            };
            if (dialog.ShowDialog() != true) return;
            using var snapshot = await viewer.CaptureSnapshotAsync(target.Acquire(includeRegion: true), raw ? SnapshotKind.Raw : SnapshotKind.Display, default).ConfigureAwait(false);
            await snapshot.SaveAsync(dialog.FileName, raw ? SnapshotEncoding.Tiff : dialog.FilterIndex switch
            { 1 => SnapshotEncoding.Png, 2 => SnapshotEncoding.Jpeg, _ => SnapshotEncoding.Bmp }).ConfigureAwait(false);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Image save failed"); }
        finally { viewer.EndSave(); }
    }
}
