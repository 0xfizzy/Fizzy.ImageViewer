using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Interaction;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ToolCleanupFailureTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("click")]
    [InlineData("close")]
    public async Task SessionAndEveryPreviewFailureSurviveCleanup(string ending)
    {
        var viewer = new Viewer(showWindow: false);
        var cancelError = new InvalidOperationException("cancel");
        var disposeError = new InvalidOperationException("dispose");
        var previewError = new InvalidOperationException("preview");
        var clickError = new InvalidOperationException("click");
        int disposed = 0, previewsDisposed = 0;
        viewer.RegisterMeasurementTool(new Tool(context =>
        {
            context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() =>
            {
                previewsDisposed++;
                throw previewError;
            });
            context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => previewsDisposed++);
            return new TestMeasurementSession(_ => throw clickError, _ => { }, () => throw cancelError, () =>
            {
                disposed++;
                throw disposeError;
            });
        }));
        viewer.ActivateMeasurementTool("cleanup");
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        AggregateException failure;
        if (ending == "close")
            failure = await Assert.ThrowsAsync<AggregateException>(() => viewer.DisposeAsync().AsTask());
        else
        {
            failure = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Assert.Throws<AggregateException>(() =>
            {
                if (ending == "cancel") viewer.EndInteraction();
                else viewer.Host.Control.Runtime.Interaction.ImageDown(1, 2);
            }));
            Assert.Equal(InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
            await viewer.DisposeAsync();
        }
        Assert.Contains(cancelError, failure.Flatten().InnerExceptions);
        Assert.Contains(disposeError, failure.Flatten().InnerExceptions);
        Assert.Contains(previewError, failure.Flatten().InnerExceptions);
        if (ending == "click") Assert.Contains(clickError, failure.Flatten().InnerExceptions);
        Assert.Equal(1, disposed);
        Assert.Equal(2, previewsDisposed);
        Assert.False(sta.IsAlive);
    }

    [Fact]
    public async Task FactoryAndPreviewCleanupFailuresAreBothReported()
    {
        await using var viewer = new Viewer(showWindow: false);
        var factoryError = new InvalidOperationException("factory");
        var previewError = new InvalidOperationException("preview");
        viewer.RegisterMeasurementTool(new Tool(context =>
        {
            context.CreateMeasurement(MeasurementGeometry.Point(new())).OnDispose(() => throw previewError);
            throw factoryError;
        }));
        var failure = Assert.Throws<AggregateException>(() => viewer.ActivateMeasurementTool("cleanup"));
        Assert.Contains(factoryError, failure.Flatten().InnerExceptions);
        Assert.Contains(previewError, failure.Flatten().InnerExceptions);
        Assert.Equal(InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
    }

    private sealed class Tool(Func<IMeasurementToolContext, IMeasurementToolSession> factory) : IMeasurementTool
    {
        public string Id => "cleanup";
        public string DisplayName => Id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => factory(context);
    }
}
