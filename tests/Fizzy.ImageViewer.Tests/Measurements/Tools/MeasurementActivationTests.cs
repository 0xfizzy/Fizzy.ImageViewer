using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementActivationTests
{
    private sealed class Tool(string id, Func<IMeasurementToolContext, IMeasurementToolSession> factory) : IMeasurementTool
    {
        public string Id => id;
        public string DisplayName => id;
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => factory(context);
    }

    [Fact]
    public async Task ViewerClosureInvalidatesRetainedRegistrationHandle()
    {
        var viewer = new Viewer(showWindow: false);
        var handle = viewer.RegisterMeasurementTool(new Tool("retained", _ =>
            new TestMeasurementSession(_ => false, _ => { }, () => { })));
        var registration = await viewer.Host.Window.Dispatcher.InvokeAsync(() => viewer.Host.Control.Runtime.Tools.FindRegistration("retained")!);
        Assert.Same(handle, registration.Handle);
        await viewer.DisposeAsync();
        Assert.Null(registration.Handle);
        await Task.Run(handle.Dispose);
        handle.Dispose();
    }

    [Fact]
    public async Task OldHandleCannotRevokeReplacementAndWorkerDisposalPreservesCompletedItems()
    {
        await using var viewer = new Viewer(showWindow: false);
        var old = viewer.RegisterMeasurementTool(new Tool("owned", _ => new TestMeasurementSession(_ => false, _ => { }, () => { })));
        viewer.UnregisterMeasurementTool("owned");
        IMeasurement? completed = null;
        var cancellations = 0;
        var replacement = viewer.RegisterMeasurementTool(new Tool("owned", context =>
        {
            completed = context.CreateMeasurement(MeasurementGeometry.Point(new()));
            completed.Complete();
            return new TestMeasurementSession(_ => false, _ => { }, () => cancellations++);
        }));
        await Task.Run(old.Dispose);
        viewer.ActivateMeasurementTool("owned");
        await Task.Run(replacement.Dispose);
        replacement.Dispose();
        Assert.Equal(1, cancellations);
        Assert.False(completed!.IsDisposed);
        Assert.Throws<KeyNotFoundException>(() => viewer.ActivateMeasurementTool("owned"));
        await viewer.DisposeAsync();
        replacement.Dispose();
    }

    [Fact]
    public async Task DisposalCanRegisterReplacementEvenWhenCancellationThrows()
    {
        await using var viewer = new Viewer(showWindow: false);
        var replacementCalls = 0;
        var disposalCalls = 0;
        var failure = new InvalidOperationException("cancel");
        var registration = viewer.RegisterMeasurementTool(new Tool("owned", _ =>
            new TestMeasurementSession(_ => false, _ => { }, () =>
            {
                viewer.RegisterMeasurementTool(new Tool("owned", _ =>
                {
                    replacementCalls++;
                    return new TestMeasurementSession(_ => false, _ => { }, () => { });
                }));
                viewer.ActivateMeasurementTool("owned");
                throw failure;
            }, () => disposalCalls++)));
        viewer.ActivateMeasurementTool("owned");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(registration.Dispose));
        registration.Dispose();
        Assert.Equal(1, replacementCalls);
        Assert.Equal(1, disposalCalls);
        Assert.Equal("owned", viewer.Host.Control.Runtime.Interaction.ActiveId);
    }

    [Fact]
    public async Task CapturedMenuToolCannotActivateSameIdReplacement()
    {
        await using var viewer = new Viewer(showWindow: false);
        var calls = 0;
        using var registration = viewer.RegisterMeasurementTool(new Tool("captured", _ =>
            new TestMeasurementSession(_ => false, _ => { }, () => { })));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Control.ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
            var item = menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, "captured"));
            registration.Dispose();
            viewer.RegisterMeasurementTool(new Tool("captured", _ =>
            {
                calls++;
                return new TestMeasurementSession(_ => false, _ => { }, () => { });
            }));
            item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Equal(0, calls);
            viewer.ActivateMeasurementTool("captured");
            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public async Task InvalidAdmissionPreservesCurrentActivationAndSharesMenuRules()
    {
        await using var viewer = new Viewer(showWindow: false);
        viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
        Assert.Throws<KeyNotFoundException>(() => viewer.ActivateMeasurementTool("missing"));
        Assert.Equal(MeasurementToolIds.Length, viewer.Host.Control.Runtime.Interaction.ActiveId);
        viewer.EndInteraction();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var registration = viewer.Host.Control.Runtime.Tools.FindRegistration(MeasurementToolIds.Length)!;
            viewer.Measurements.IsVisible = false;
            Assert.Equal(MeasurementActivationResult.Hidden, viewer.Host.Control.Runtime.Interaction.ActivateMeasurementTool(registration));
            Assert.Throws<InvalidOperationException>(() => viewer.ActivateMeasurementTool(registration.Id));
            viewer.Measurements.IsVisible = true;
            viewer.Measurements.IsHitTestVisible = false;
            Assert.Equal(MeasurementActivationResult.InputDisabled, viewer.Host.Control.Runtime.Interaction.ActivateMeasurementTool(registration));
            Assert.Throws<InvalidOperationException>(() => viewer.ActivateMeasurementTool(registration.Id));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RevokedTargetIsNeverActivatedFromOutgoingCleanup(bool dispose, bool replace)
    {
        await using var viewer = new Viewer(showWindow: false);
        var oldCalls = 0;
        var newCalls = 0;
        viewer.RegisterMeasurementTool(new Tool("target", _ => { oldCalls++; return new TestMeasurementSession(_ => false, _ => { }, () => { }); }));
        void Revoke()
        {
            viewer.UnregisterMeasurementTool("target");
            if (replace) viewer.RegisterMeasurementTool(new Tool("target", _ => { newCalls++; return new TestMeasurementSession(_ => false, _ => { }, () => { }); }));
        }
        viewer.RegisterMeasurementTool(new Tool("outgoing", _ => new TestMeasurementSession(_ => false, _ => { },
            () => { if (!dispose) Revoke(); }, () => { if (dispose) Revoke(); })));
        viewer.ActivateMeasurementTool("outgoing");
        viewer.ActivateMeasurementTool("target");
        Assert.Equal(0, oldCalls);
        Assert.Equal(0, newCalls);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(viewer.Host.Control.Runtime.Interaction.ActiveId);
            Assert.Equal(InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
            Assert.False(viewer.Layers.Collection.InputSuppressed);
        });
        if (replace) { viewer.ActivateMeasurementTool("target"); Assert.Equal(1, newCalls); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackFailureRetainsCancellationAndDisposalFailures(bool move)
    {
        await using var viewer = new Viewer(showWindow: false);
        viewer.RegisterMeasurementTool(new Tool("failure", _ => new TestMeasurementSession(
            _ => throw new InvalidOperationException("click"), _ => throw new InvalidOperationException("move"),
            () => throw new InvalidOperationException("cancel"), () => throw new InvalidOperationException("dispose"))));
        viewer.ActivateMeasurementTool("failure");
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var error = Assert.Throws<AggregateException>(() =>
            {
                if (move) viewer.Host.Control.Runtime.Interaction.ImageMove(0, 0); else viewer.Host.Control.Runtime.Interaction.ImageDown(0, 0);
            });
            Assert.Equal(new[] { move ? "move" : "click", "cancel", "dispose" }, error.Flatten().InnerExceptions.Select(e => e.Message));
            Assert.Equal(InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
        });
    }

    [Fact]
    public async Task WorkerFinishOnlyEndsItsOwnActivationAndPreservesOrigin()
    {
        await using var viewer = new Viewer(showWindow: false);
        var contexts = new List<IMeasurementToolContext>();
        var items = new List<IMeasurement>();
        viewer.RegisterMeasurementTool(new Tool("async", context =>
        {
            contexts.Add(context);
            items.Add(context.CreateMeasurement(MeasurementGeometry.Line(new(), new(1, 1))));
            return new TestMeasurementSession(_ => false, _ => { }, () => { });
        }));
        viewer.ActivateMeasurementTool("async");
        items[0].Complete();
        var origin = items[0].Origin;
        viewer.ActivateMeasurementTool("async");
        Assert.False(await Task.Run(contexts[0].Finish));
        Assert.NotEqual(origin.SessionId, contexts[1].Origin.SessionId);
        Assert.Equal("async", origin.ToolId);
        Assert.Equal(origin, items[0].Origin);
        Assert.False(items[1].IsDisposed);
        Assert.True(await Task.Run(contexts[1].Finish));
        Assert.True(items[1].IsDisposed);
        Assert.False(items[0].IsDisposed);
        Assert.False(await Task.Run(contexts[1].Finish));
        await viewer.DisposeAsync();
        Assert.False(contexts[1].Finish());
    }

    [Fact]
    public async Task FactoryCanFinishNormallyBeforeTransferringItsCallback()
    {
        await using var viewer = new Viewer(showWindow: false);
        var cancellations = 0;
        var disposals = 0;
        viewer.RegisterMeasurementTool(new Tool("factory-finish", context =>
        {
            context.CreateMeasurement(MeasurementGeometry.Point(new())).Complete();
            Assert.True(context.Finish());
            return new TestMeasurementSession(_ => false, _ => { }, () => cancellations++, () => disposals++);
        }));
        viewer.ActivateMeasurementTool("factory-finish");
        Assert.Equal(0, cancellations);
        Assert.Equal(1, disposals);
        Assert.Equal(InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
    }

    [Fact]
    public async Task DeleteKeyCleanupFailureDoesNotTerminateRealDispatcher()
    {
        await using var viewer = new Viewer(showWindow: false);
        var closed = 0;
        viewer.Closed += (_, _) => closed++;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var item = (MeasurementItem)new MeasurementCreationContext(viewer.Host.Control.Runtime.Measurements, viewer.Host.Control.Runtime.MeasurementRuntime, viewer.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            item.Complete();
            item.OnDispose(() => throw new InvalidOperationException("cleanup"));
            viewer.Host.Control.Runtime.Interaction.Select(item);
            var container = viewer.Host.Control.ImageViewport.Container;
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestSource(container), 0, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            container.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.True(item.IsDisposed);
        });
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => Assert.False(viewer.Host.Window.Dispatcher.HasShutdownStarted));
        Assert.Equal(0, closed);
        await viewer.DisposeAsync();
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task UnexpectedDispatcherExitRaisesClosedExactlyOnce()
    {
        var viewer = new Viewer(showWindow: false);
        var sta = await viewer.Host.Window.Dispatcher.InvokeAsync(() => Thread.CurrentThread);
        var failure = new InvalidOperationException("unexpected");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        viewer.Closed += (_, _) => { count++; closed.TrySetResult(); };
        _ = viewer.Host.Window.Dispatcher.BeginInvoke(new Action(() => throw failure));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.DisposeAsync().AsTask()));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => viewer.DisposeAsync().AsTask()));
        Assert.Equal(1, count);
        Assert.False(sta.IsAlive);
        Assert.True(viewer.Host.Window.Dispatcher.HasShutdownFinished);
    }

    private sealed class TestSource(System.Windows.Media.Visual root) : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = root;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
}
