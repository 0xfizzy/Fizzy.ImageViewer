using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public sealed class MeasurementUnregisterTests
{
    private sealed class Tool(Viewer viewer) : IMeasurementTool
    {
        public string Id => "reentrant";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
            => new TestMeasurementSession(point => OnClick(point, context),
                point => OnMouseMove(point, context), () => Cancel(context));
        public bool OnClick(Point point, IMeasurementToolContext context) => false;
        public void OnMouseMove(Point point, IMeasurementToolContext context) { }
        public void Cancel(IMeasurementToolContext context) => viewer.StartMeasurement(Id);
    }

    [Fact]
    public async Task UnregisterReentrantCancellationLeavesInteractionIdle()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.RegisterMeasurementTool(new Tool(viewer));
            viewer.StartMeasurement("reentrant");
            Assert.Throws<KeyNotFoundException>(() => viewer.UnregisterMeasurementTool("reentrant"));
            Assert.Equal(Interaction.InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.InputSuppressed);
        });
    }
}
