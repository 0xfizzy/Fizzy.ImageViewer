using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interaction;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ToolSessionLifetimeTests
{
    private sealed class Tool(Func<IMeasurementToolContext, IMeasurementToolSession> factory) : IMeasurementTool
    {
        public string Id => "lifetime";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => factory(context);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("failure")]
    [InlineData("close")]
    [InlineData("cancel-failure")]
    public async Task EveryTerminalPathDisposesSessionExactlyOnce(string ending)
    {
        await using var viewer = new Viewer(showWindow: false);
        int disposed = 0, cancelled = 0;
        IMeasurement? preview = null;
        viewer.RegisterMeasurementTool(new Tool(context =>
        {
            preview = context.CreateMeasurement(MeasurementGeometry.Point(new()));
            return new TestMeasurementSession(_ =>
            {
                if (ending == "failure") throw new InvalidOperationException("click");
                preview.Complete();
                return true;
            }, _ => { }, () =>
            {
                cancelled++;
                if (ending == "cancel-failure") throw new InvalidOperationException("cancel");
            }, () =>
            {
                viewer.Host.Window.Dispatcher.VerifyAccess();
                Assert.Throws<ObjectDisposedException>(() => context.CreateMeasurement(MeasurementGeometry.Point(new())));
                disposed++;
            });
        }));
        viewer.ActivateMeasurementTool("lifetime");
        if (ending == "close") await viewer.DisposeAsync();
        else await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            if (ending == "failure") Assert.Throws<InvalidOperationException>(() => viewer.Host.Interaction.ImageDown(1, 2));
            else if (ending == "cancel-failure") Assert.Throws<InvalidOperationException>(viewer.EndInteraction);
            else if (ending == "cancel") viewer.EndInteraction();
            else viewer.Host.Interaction.ImageDown(1, 2);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
        Assert.Equal(1, disposed);
        Assert.Equal(ending == "complete" ? 0 : 1, cancelled);
        Assert.Equal(ending != "complete", preview!.IsDisposed);
        await viewer.DisposeAsync();
        Assert.Equal(1, disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalReentryAndFailureCannotEndReplacement(bool fail)
    {
        await using var viewer = new Viewer(showWindow: false);
        int disposed = 0;
        viewer.RegisterMeasurementTool(new Tool(_ => new TestMeasurementSession(_ => true, _ => { }, () => { }, () =>
        {
            disposed++;
            viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
            if (fail) throw new InvalidOperationException("dispose");
        })));
        viewer.ActivateMeasurementTool("lifetime");
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            if (fail) Assert.Throws<InvalidOperationException>(() => viewer.Host.Interaction.ImageDown(0, 0));
            else viewer.Host.Interaction.ImageDown(0, 0);
            Assert.Equal(MeasurementToolIds.Length, viewer.Host.Interaction.ActiveId);
            Assert.Equal(InteractionMode.Measuring, viewer.Host.Interaction.Mode);
            Assert.True(viewer.Layers.Collection.InputSuppressed);
        });
        Assert.Equal(1, disposed);
    }

    [Fact]
    public async Task SupersededFactoryResultIsCancelledAndDisposed()
    {
        await using var viewer = new Viewer(showWindow: false);
        int cancelled = 0, disposed = 0;
        viewer.RegisterMeasurementTool(new Tool(_ =>
        {
            viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
            return new TestMeasurementSession(_ => false, _ => { }, () => cancelled++, () => disposed++);
        }));
        viewer.ActivateMeasurementTool("lifetime");
        Assert.Equal(1, cancelled);
        Assert.Equal(1, disposed);
        Assert.Equal(MeasurementToolIds.Length, viewer.Host.Interaction.ActiveId);
    }
}
