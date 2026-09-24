using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Hud;
using Fizzy.ImageViewer.Snapshots;

namespace Fizzy.ImageViewer.Menus;

/// <summary>Composes built-in actions and captures their opening targets. Services are borrowed.</summary>
internal sealed class ViewerMenuController : IDisposable
{
    private readonly MenuManager _menus;
    private readonly MeasurementInteractionCoordinator _interaction;
    private readonly MeasurementToolRegistry _tools;
    private readonly ViewerLayers _layers;
    private readonly MenuSnapshotSession _session;
    private readonly SnapshotCapture _capture;
    private readonly PixelInfoController _pixelInfo;
    private IDisposable? _registration;

    internal ViewerMenuController(MenuManager menus, MeasurementInteractionCoordinator interaction,
        MeasurementToolRegistry tools, ViewerLayers layers, MenuSnapshotSession session,
        SnapshotCapture capture, PixelInfoController pixelInfo)
    {
        _menus = menus; _interaction = interaction; _tools = tools; _layers = layers;
        _session = session; _capture = capture; _pixelInfo = pixelInfo;
        try
        {
            _registration = menus.Register(CreateItems);
            menus.Opening += CaptureTarget;
            menus.Closing += CloseTarget;
        }
        catch { Dispose(); throw; }
    }

    internal void CaptureTarget() => _session.Open(descriptor =>
        _interaction.SelectedMeasurement is { IsComplete: true, Geometry: RectangleMeasurementGeometry } item
            ? ((RectangleMeasurementGeometry)item.Geometry).ToRegion(descriptor) : null);

    private void CloseTarget() => _session.Close();

    private IEnumerable<IMenuItem> CreateItems()
    {
        foreach (var item in CreateInteractionItems()) yield return item;
        yield return SeparatorMenuItem.Instance;
        yield return new MenuItem("Clear All Shapes", _layers.ClearContents);
        yield return new SaveImageMenuItem(_session, _capture, false);
        yield return new SaveImageMenuItem(_session, _capture, true);
        yield return new SaveImageMenuItem(_session, _capture, false, true);
        yield return new SaveImageMenuItem(_session, _capture, true, true);
        yield return SeparatorMenuItem.Instance;
        yield return new CheckableMenuItem("Pixel Info", () => _pixelInfo.IsEnabled, () =>
        {
            _pixelInfo.IsEnabled = !_pixelInfo.IsEnabled;
        });
    }

    private IEnumerable<IMenuItem> CreateInteractionItems()
    {
        var selected = _interaction.SelectedMeasurement;
        if (_interaction.Mode == InteractionMode.Measuring)
        {
            yield return new MenuItem("Cancel Measurement", _interaction.Cancel);
            yield break;
        }
        if (selected != null)
        {
            yield return new MenuItem("Edit", () => { if (!selected.IsDisposed) _interaction.StartEditing(selected); });
            yield return new MenuItem("Delete", () => { if (!selected.IsDisposed) _interaction.Delete(selected); });
            yield return SeparatorMenuItem.Instance;
        }
        foreach (var tool in _tools.RegisteredTools)
            yield return new MenuItem(tool.DisplayName, () =>
            {
                _interaction.ActivateMeasurementTool(tool);
            });
    }

    public void Dispose()
    {
        _menus.Opening -= CaptureTarget;
        _menus.Closing -= CloseTarget;
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }
}
