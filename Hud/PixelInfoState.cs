using Fizzy.ImageViewer.Imaging.Queries;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using System.Globalization;

namespace Fizzy.ImageViewer.Hud;

/// <summary>UI-thread state; sampling and time belong to PixelQueryScheduler.</summary>
internal sealed class PixelInfoState(Action<string?> display) : IFrameQueryClient
{
    private readonly Guid _id = Guid.NewGuid();
    private FrameDescriptor? _descriptor;
    private bool _enabled, _inside, _hasValue;
    private double _x, _y;
    private long _positionVersion, _sessionVersion;
    private string? _text;

    public QueryPolicy Policy => new(true, 10, TimeSpan.FromMilliseconds(300));

    public void Enable() { _enabled = true; _sessionVersion++; }
    public void Disable() { _enabled = false; Leave(); }
    public void Leave()
    {
        _inside = false;
        _sessionVersion++;
        ClearResult();
    }

    public void Move(double x, double y)
    {
        bool valid = double.IsFinite(x) && double.IsFinite(y) && x >= 0 && y >= 0 &&
            (_descriptor is not { } d || (x < d.Width && y < d.Height));
        if (!_inside || Math.Floor(x) != Math.Floor(_x) || Math.Floor(y) != Math.Floor(_y)) _positionVersion++;
        _x = x; _y = y;
        if (!valid) { if (_inside) Leave(); return; }
        if (!_inside) _sessionVersion++;
        _inside = true;
        // A valid result is retained as one complete coordinate/value pair.
        if (!_hasValue) ShowWaiting();
    }

    public QueryRequest? Capture(FrameDescriptor descriptor)
    {
        if (_descriptor != descriptor)
        {
            _descriptor = descriptor;
            _sessionVersion++;
            _hasValue = false;
        }
        if (!_enabled || !_inside) return null;
        if (_x >= descriptor.Width || _y >= descriptor.Height) { Leave(); return null; }
        if (!_hasValue) ShowWaiting();
        int x = (int)Math.Floor(_x), y = (int)Math.Floor(_y);
        return new PixelQueryRequest(new(_id, _positionVersion, _sessionVersion), [new(x, y)], (_, samples) =>
        {
            _hasValue = true;
            SetText(Format(descriptor, x, y, samples[0]));
        });
    }

    public void InvalidateResult(ResultInvalidation reason)
    {
        switch (reason)
        {
            case ResultInvalidation.CoordinatesChanged:
                if (!_hasValue) ShowWaiting();
                break;
            case ResultInvalidation.NoFrame:
                // Invalidate in-flight work even if the next frame has the same descriptor.
                _sessionVersion++;
                ClearResult();
                break;
            case ResultInvalidation.NoTarget:
                ClearResult();
                break;
            default:
                _hasValue = false;
                ShowWaiting();
                break;
        }
    }

    public void ClearResult() { _hasValue = false; SetText(null); }
    private void ShowWaiting()
    {
        if (_enabled && _inside && _descriptor is { } d)
            SetText(Format(d, (int)Math.Floor(_x), (int)Math.Floor(_y), null));
    }
    private void SetText(string? text)
    {
        if (_text == text) return;
        _text = text;
        display(text);
    }

    internal static string Format(FrameDescriptor descriptor, int x, int y, PixelSample? sample)
    {
        static string Coordinate(int value, int maximum) => value.ToString(CultureInfo.InvariantCulture)
            .PadLeft(maximum.ToString(CultureInfo.InvariantCulture).Length);
        static string Value(double? value) => (value?.ToString("G7", CultureInfo.InvariantCulture) ?? "—").PadLeft(7);
        var coordinates = $"X: {Coordinate(x, descriptor.Width - 1)}, Y: {Coordinate(y, descriptor.Height - 1)} | ";
        if (descriptor.Format is FramePixelFormat.Gray8 or FramePixelFormat.Gray16 or FramePixelFormat.Gray32Float)
            return coordinates + $"GRAY: {Value(sample?.Gray)}";
        return coordinates + $"R: {Value(sample?.R)}, G: {Value(sample?.G)}, B: {Value(sample?.B)}, A: {Value(sample?.A)}" +
            (descriptor.Format == FramePixelFormat.Pbgra32 ? " (premultiplied)" : "");
    }
}
