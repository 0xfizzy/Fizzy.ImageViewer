using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Measurements.Presentation;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class LayerInteractionBoundaryTests
{
    private sealed class Tool(string id, Func<IMeasurementToolContext, IMeasurementToolSession> factory) : IMeasurementTool
    {
        public string Id => id;
        public string DisplayName => id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => factory(context);
    }

    [Fact]
    public async Task DisabledHitTestingRejectsToolAndEditAdmissionUntilReenabled()
    {
        await using var viewer = new Viewer(showWindow: false);
        var factories = 0;
        viewer.RegisterMeasurementTool(new Tool("admission", _ =>
        {
            factories++;
            return new TestMeasurementSession(_ => false, _ => { }, () => { });
        }));
        viewer.Layers.Measurements.IsHitTestVisible = false;
        Assert.Throws<InvalidOperationException>(() => viewer.ActivateMeasurementTool("admission"));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            viewer.Host.Interaction.StartEditing(item);
            viewer.Host.Interaction.ActivateMeasurementTool("admission");
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Host.Interaction.Editor.IsEditing);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
        Assert.Equal(0, factories);
        viewer.Layers.Measurements.IsHitTestVisible = true;
        viewer.ActivateMeasurementTool("admission");
        Assert.Equal(1, factories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingHitTestingRejectsRestartFromSessionCleanup(bool restartFromDispose)
    {
        await using var viewer = new Viewer(showWindow: false);
        var factories = 0;
        IMeasurement? preview = null;
        void Restart() => Assert.Throws<InvalidOperationException>(() => viewer.ActivateMeasurementTool("restart"));
        viewer.RegisterMeasurementTool(new Tool("restart", context =>
        {
            factories++;
            preview = context.CreateMeasurement(MeasurementGeometry.Point(new()));
            return new TestMeasurementSession(_ => false, _ => { },
                () => { if (!restartFromDispose) Restart(); },
                () => { if (restartFromDispose) Restart(); });
        }));
        viewer.ActivateMeasurementTool("restart");
        viewer.Layers.Measurements.IsHitTestVisible = false;
        Assert.Equal(1, factories);
        Assert.True(preview!.IsDisposed);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(viewer.Host.Interaction.ActiveId);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
    }

    [Fact]
    public async Task OutgoingCancellationDisablingHitTestingPreventsPendingToolFactory()
    {
        await using var viewer = new Viewer(showWindow: false);
        var factories = 0;
        viewer.RegisterMeasurementTool(new Tool("outgoing", _ => new TestMeasurementSession(
            _ => false, _ => { }, () => viewer.Layers.Measurements.IsHitTestVisible = false)));
        viewer.RegisterMeasurementTool(new Tool("target", _ =>
        {
            factories++;
            return new TestMeasurementSession(_ => false, _ => { }, () => { });
        }));
        viewer.ActivateMeasurementTool("outgoing");
        viewer.ActivateMeasurementTool("target");
        Assert.Equal(0, factories);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(viewer.Host.Interaction.ActiveId);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
    }

    [Fact]
    public async Task FactoryDisablingHitTestingCannotReviveItsEndedActivation()
    {
        await using var viewer = new Viewer(showWindow: false);
        var cancellations = 0;
        var disposals = 0;
        IMeasurement? preview = null;
        viewer.RegisterMeasurementTool(new Tool("factory-disable", context =>
        {
            preview = context.CreateMeasurement(MeasurementGeometry.Point(new()));
            viewer.Layers.Measurements.IsHitTestVisible = false;
            return new TestMeasurementSession(_ => false, _ => { }, () => cancellations++, () => disposals++);
        }));
        viewer.ActivateMeasurementTool("factory-disable");
        Assert.True(preview!.IsDisposed);
        Assert.Equal(1, cancellations);
        Assert.Equal(1, disposals);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(viewer.Host.Interaction.ActiveId);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
    }

    [Fact]
    public async Task RoutedInputResolvesBothMeasurementVisualsAndRejectsUnregisteredVisuals()
    {
        await using var viewer = new Viewer(showWindow: false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Point(new(1, 2)));
            foreach (var visual in item.Presentation.Visuals)
            {
                viewer.Host.Interaction.ClearSelection();
                var click = Click(visual);
                Assert.True(click.Handled);
                Assert.Same(item, viewer.Host.Interaction.SelectedMeasurement);
            }
            var orphan = MeasurementVisualFactory.CreatePoint(new(5, 6));
            viewer.Host.Window.MeasurementOverlay.AddVisual(orphan);
            viewer.Host.Interaction.ClearSelection();
            Assert.False(Click(orphan).Handled);
            Assert.Null(viewer.Host.Interaction.SelectedMeasurement);
            Assert.False(viewer.Host.Interaction.Editor.IsEditing);
        });
    }

    [Fact]
    public async Task SemanticInputRejectsForeignAndDisposedMeasurementOwners()
    {
        await using var viewer = new Viewer(showWindow: false);
        await using var other = new Viewer(showWindow: false);
        var foreign = await other.Host.Window.Dispatcher.InvokeAsync(() =>
            (MeasurementItem)new MeasurementCreationContext(other.Host.Measurements, other.Host.MeasurementRuntime, other.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Point(new())));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var context = new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame);
            var selected = (MeasurementItem)context.CreateMeasurement(MeasurementGeometry.Point(new()));
            var removed = (MeasurementItem)context.CreateMeasurement(MeasurementGeometry.Point(new(1, 1)));
            removed.Dispose();
            viewer.Host.Interaction.Select(selected);
            foreach (var invalid in new[] { foreign, removed })
            {
                Assert.False(viewer.Host.Interaction.PointerDown(invalid, new()));
                viewer.Host.Interaction.StartEditing(invalid);
                viewer.Host.Interaction.Delete(invalid);
                Assert.Same(selected, viewer.Host.Interaction.SelectedMeasurement);
                Assert.False(viewer.Host.Interaction.Editor.IsEditing);
            }
            Assert.False(selected.IsDisposed);
            Assert.False(foreign.IsDisposed);
        });
    }

    private static MouseButtonEventArgs Click(UIElement visual)
    {
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = Mouse.MouseDownEvent };
        visual.RaiseEvent(click);
        return click;
    }
}
