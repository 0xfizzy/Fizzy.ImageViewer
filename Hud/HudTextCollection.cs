using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Hud;

/// <summary>Owns HUD handles and visuals on the viewer STA.</summary>
internal sealed class HudTextCollection(HudLayer layer, ViewerLifetime lifetime) : IDisposable
{
    private readonly Dictionary<HudTextHandle, TextBlock> _texts = [];
    private bool _closed;

    internal HudTextHandle Add(string text, Brush brush, Point? anchor, AnchorAlignment alignment, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        var frozen = HudTextHandle.SnapshotBrush(brush);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (anchor is { } point && (!double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        return lifetime.Invoke(layer.Dispatcher, () =>
        {
            var textBlock = anchor.HasValue ? layer.AddTextAt(text, frozen, fontSize, anchor.Value, alignment)
                : layer.AddText(text, frozen, fontSize);
            HudTextHandle? handle = null;
            handle = new((nextText, nextBrush) => lifetime.Invoke(layer.Dispatcher, () =>
            {
                ObjectDisposedException.ThrowIf(handle!.IsDisposed, handle);
                layer.UpdateText(textBlock, nextText, nextBrush);
                return true;
            }), () => Remove(handle!));
            _texts.Add(handle, textBlock);
            return handle;
        });
    }

    private void Remove(HudTextHandle handle)
    {
        lifetime.InvokeRemoval(layer.Dispatcher, () =>
        {
            if (_texts.Remove(handle, out var text)) layer.RemoveText(text);
        });
    }

    public void Dispose()
    {
        layer.Dispatcher.VerifyAccess();
        if (_closed) return;
        _closed = true;
        foreach (var (handle, text) in _texts)
        {
            handle.Invalidate();
            layer.RemoveText(text);
        }
        _texts.Clear();
    }
}
