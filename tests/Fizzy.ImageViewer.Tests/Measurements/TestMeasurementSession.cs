using Fizzy.ImageViewer.Measurements;
using System.Windows;

namespace Fizzy.ImageViewer.Tests;

/// <summary>Independent callback session for test probes; counters and injected callbacks belong to the fixture.</summary>
internal sealed class TestMeasurementSession(
    Func<Point, bool> click, Action<Point> move, Action cancel) : IMeasurementToolSession
{
    public bool OnClick(Point point) => click(point);
    public void OnMouseMove(Point point) => move(point);
    public void Cancel() => cancel();
}
