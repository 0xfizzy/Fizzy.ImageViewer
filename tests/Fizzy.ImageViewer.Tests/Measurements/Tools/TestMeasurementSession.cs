using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Tests;

/// <summary>Independent callback session for test probes; counters and injected callbacks belong to the fixture.</summary>
internal sealed class TestMeasurementSession(
    Func<Point, bool> click, Action<Point> move, Action cancel, Action? dispose = null) : IMeasurementToolSession
{
    public MeasurementClickResult OnClick(Point point) => click(point) ? MeasurementClickResult.Finish : MeasurementClickResult.Continue;
    public void OnMouseMove(Point point) => move(point);
    public void Cancel() => cancel();
    public void Dispose() => dispose?.Invoke();
}
