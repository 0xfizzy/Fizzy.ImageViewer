using Fizzy.ImageViewer.Internal;
using Fizzy.ImageViewer.Enums;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    public Drawing.ViewerLayers Layers => _window.Layers;

    /// <summary>Draws a non-interactive single-element batch in the Markers layer.</summary>
    public IDisposable DrawLine(Point p1, Point p2, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.LineElement(p1, p2, brush, thickness)]);

    public IDisposable DrawText(Point anchor, string text, Brush brush, int fontSize = 14, Vector offset = default) =>
        Layers.Markers.AddBatch([new Drawing.TextElement(anchor, text, brush, fontSize, offset)]);

    public IDisposable DrawCrosshair(Point center, Brush brush, double size = 20, double thickness = 2) =>
        Layers.Markers.AddBatch([new Drawing.CrosshairElement(center, brush, size, thickness)]);

    public IDisposable DrawRectangle(Rect rect, Brush brush, double thickness = 1.0) =>
        Layers.Markers.AddBatch([new Drawing.RectangleElement(rect, brush, thickness)]);

    public IDisposable DrawCircle(Point center, double radius, Brush brush, double thickness = 1.0, Brush? fill = null) =>
        Layers.Markers.AddBatch([new Drawing.CircleElement(center, radius, brush, thickness, fill)]);

    /// <summary>Clears all business layers, including measurements, without clearing the HUD.</summary>
    public void ClearShapes() => Layers.Clear();
    private readonly Dictionary<Interfaces.HudTextHandle, TextBlock> _hudTexts = [];

    /// <summary>Creates HUD text with fixed layout. Update text and color through the returned handle.</summary>
    public Interfaces.HudTextHandle DrawHudText(string text, Brush brush,
        Point? anchor = null, AnchorAlignment alignment = AnchorAlignment.TopLeft, double fontSize = 14)
    {
        ArgumentNullException.ThrowIfNull(text);
        var frozen = Interfaces.HudTextHandle.SnapshotBrush(brush);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (anchor is { } point && (!double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        return InvokeAlive(() =>
        {
            var tb = anchor.HasValue ? _window.Layer2.AddTextAt(text, frozen, fontSize, anchor.Value, alignment)
                : _window.Layer2.AddText(text, frozen, fontSize);
            Interfaces.HudTextHandle? handle = null;
            handle = new Interfaces.HudTextHandle((nextText, nextBrush) => InvokeAlive(() =>
            {
                ObjectDisposedException.ThrowIf(handle!.IsDisposed, handle);
                _window.Layer2.UpdateText(tb, nextText, nextBrush);
            }), () => RemoveHud(handle!));
            _hudTexts.Add(handle, tb);
            return handle;
        });
    }

    private void RemoveHud(Interfaces.HudTextHandle handle)
    {
        if (_lifetime.IsStopping) return;
        try { _window.Dispatcher.Invoke(() =>
        {
            if (_hudTexts.Remove(handle, out var text)) _window.Layer2.RemoveText(text);
        }); }
        catch (TaskCanceledException) when (_closed) { }
        catch (InvalidOperationException) when (_closed) { }
    }

    private void CloseHud()
    {
        foreach (var (handle, text) in _hudTexts)
        {
            handle.Invalidate();
            _window.Layer2.RemoveText(text);
        }
        _hudTexts.Clear();
    }
}
