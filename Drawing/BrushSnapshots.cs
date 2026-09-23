using System.Windows.Media;

namespace Fizzy.ImageViewer.Drawing;

internal static class BrushSnapshots
{
    internal static Brush Freeze(Brush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (brush.IsFrozen) return brush;
        var copy = brush.CloneCurrentValue();
        if (!copy.CanFreeze) throw new ArgumentException("Brush must support freezing.", nameof(brush));
        copy.Freeze();
        return copy;
    }

    internal static Brush Copy(Brush brush, ref Dictionary<Brush, Brush>? cache)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (brush.IsFrozen) return brush;
        cache ??= new(ReferenceEqualityComparer.Instance);
        if (cache.TryGetValue(brush, out var copy)) return copy;
        brush.VerifyAccess();
        copy = Freeze(brush);
        cache.Add(brush, copy);
        return copy;
    }
}
