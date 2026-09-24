using Fizzy.ImageViewer.Viewport;
using Fizzy.ImageViewer.Interaction;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class ViewportPanTests
{
    private sealed class FakeCapture : IMouseCapture
    {
        public bool IsCaptured { get; set; }
        public bool Succeeds = true;
        public int Releases;
        public Action? Released;
        public bool Capture() => IsCaptured = Succeeds;
        public void Release() { IsCaptured = false; Releases++; Released?.Invoke(); }
    }

    [Fact]
    public void FailedCaptureNeverStartsOrMovesTheViewport()
    {
        var capture = new FakeCapture { Succeeds = false };
        var pan = new ViewportPan(capture);
        Assert.False(pan.Begin(new(10, 20)));
        Assert.False(pan.IsActive);
        Assert.Equal(default(Vector), pan.Move(new(30, 40)));
        pan.End();
        Assert.Equal(0, capture.Releases);
    }

    [Fact]
    public void LostCaptureStopsMovementAndAllowsAnotherDrag()
    {
        var capture = new FakeCapture();
        var pan = new ViewportPan(capture);
        Assert.True(pan.Begin(new(10, 20)));
        Assert.Equal(new Vector(3, -2), pan.Move(new(13, 18)));
        capture.IsCaptured = false;
        pan.LostCapture();
        Assert.Equal(default(Vector), pan.Move(new(40, 50)));
        Assert.True(pan.Begin(new(30, 40)));
        Assert.Equal(new Vector(-1, 2), pan.Move(new(29, 42)));
        capture.Released = () => { Assert.False(pan.IsActive); pan.End(); };
        pan.End();
        pan.End();
        Assert.Equal(1, capture.Releases);
        Assert.Equal(default(Vector), pan.Move(new(50, 60)));
    }
}
