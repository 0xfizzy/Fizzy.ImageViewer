using Microsoft.Win32;
using Fizzy.ImageViewer.Snapshots;
using System.Windows;
using System.Windows.Threading;

namespace Fizzy.ImageViewer.Menus;

internal sealed class SaveImageMenuItem(MenuSnapshotSession session, SnapshotCapture capture, bool raw, bool region = false) : IMenuItem
{
    public bool IsVisible => !region || session.HasRegion;
    public string Header => region ? (raw ? "Export Region Raw TIFF..." : "Save Region Display Image As...") : raw ? "Export Raw TIFF..." : "Save Display Image As...";
    public async ValueTask ExecuteAsync()
    {
        if (!session.TryBeginSave()) return;
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            using var target = session.AcquireTarget(region);
            if (target == null) { MessageBox.Show("No image to save."); return; }
            var dialog = new SaveFileDialog
            {
                Filter = raw ? "TIFF Image|*.tif" : "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                FileName = region ? "Region" : "Image"
            };
            if (dialog.ShowDialog() != true) return;
            using var snapshot = await capture.CaptureAsync(target.View.Acquire(), raw ? SnapshotKind.Raw : SnapshotKind.Display, target.Region).ConfigureAwait(false);
            await snapshot.SaveAsync(dialog.FileName, raw ? SnapshotEncoding.Tiff : dialog.FilterIndex switch
            { 1 => SnapshotEncoding.Png, 2 => SnapshotEncoding.Jpeg, _ => SnapshotEncoding.Bmp }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Report on the original STA only while it is available; the binding also logs the failure.
            try
            {
                if (!dispatcher.HasShutdownStarted)
                    _ = dispatcher.BeginInvoke(() =>
                    {
                        if (!dispatcher.HasShutdownStarted) MessageBox.Show(ex.Message, "Image save failed");
                    });
            }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted) { }
            throw;
        }
        finally { session.EndSave(); }
    }
}
