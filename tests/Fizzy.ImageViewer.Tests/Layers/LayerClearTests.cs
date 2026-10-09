using Fizzy.ImageViewer.Layers;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class LayerClearTests
{
    [Fact]
    public async Task CancellationAndContentCleanupFailuresAreBothPreservedAndGateIsReset()
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var layer = new FailingLayer(viewer.Layers.Collection);
            var cancellation = new InvalidOperationException("cancel");
            layer.Clearing += () => throw cancellation;
            var error = Assert.Throws<AggregateException>(layer.Clear);
            Assert.Collection(error.InnerExceptions,
                first => Assert.Same(cancellation, first),
                second => Assert.Same(layer.CleanupError, second));
            Assert.Equal(1, layer.Clears);
            Assert.False(layer.IsClearing);
            Assert.Throws<AggregateException>(layer.Clear);
            Assert.Equal(2, layer.Clears);
        });
    }

    private sealed class FailingLayer(LayerCollection owner) : ViewerLayer(owner, "test", 0, false, false)
    {
        public int Clears;
        public Exception CleanupError { get; } = new InvalidOperationException("content cleanup");
        internal override void ClearContent() { Clears++; throw CleanupError; }
        internal override void Redraw(bool scaleOnly) { }
    }
}
