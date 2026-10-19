using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using System.Runtime.CompilerServices;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class EventSubscriptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FrameSubscriptionsUseSnapshotsAndPreserveDuplicateSemantics(bool standalone) => ControlTestDispatcher.Run(async () =>
    {
        await using IViewer owner = standalone ? new Viewer(showWindow: false) : new ImageViewerControl();
        IViewerCapabilities view = owner;
        var order = new List<string>();
        var senders = new List<object?>();
        bool changed = false;
        EventHandler<FrameInfo> second = (sender, _) => { senders.Add(sender); order.Add("second"); };
        EventHandler<FrameInfo> late = (_, _) => order.Add("late");
        EventHandler<FrameInfo> duplicate = (_, _) => order.Add("duplicate");
        view.FrameCommitted += (_, _) =>
        {
            order.Add("first");
            if (changed) return;
            changed = true;
            view.FrameCommitted -= second;
            view.FrameCommitted += late;
        };
        view.FrameCommitted += second;
        view.FrameCommitted += (_, _) => throw new InvalidOperationException("subscriber");
        view.FrameCommitted += duplicate;
        view.FrameCommitted += duplicate;
        view.FrameCommitted -= duplicate;
        await Submit(view);
        Assert.Equal(new[] { "first", "second", "duplicate" }, order);
        Assert.Same(owner, Assert.Single(senders));
        order.Clear();
        await Submit(view);
        Assert.Equal(new[] { "first", "duplicate", "late" }, order);
        await owner.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => view.FrameCommitted += second);
        view.FrameCommitted -= second;
        view.FrameCommitted -= second;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FacadeSubscriptionsRejectStoppingAndReleaseRacingSubscribers(bool standalone) => ControlTestDispatcher.Run(async () =>
    {
        await using IViewer owner = standalone ? new Viewer(showWindow: false) : new ImageViewerControl();
        var retained = await RaceSubscriptionWithDisposal(owner);
        var subscriber = new Subscriber();
        Assert.Throws<ObjectDisposedException>(() => owner.FrameCommitted += subscriber.Frame);
        Assert.Throws<ObjectDisposedException>(() => owner.MeasurementCompleted += subscriber.Measurement);
        Assert.Throws<ObjectDisposedException>(() => owner.MeasurementChanged += subscriber.Measurement);
        Assert.Throws<ObjectDisposedException>(() => owner.MeasurementRemoved += subscriber.Measurement);
        for (int i = 0; i < 2; i++)
        {
            owner.FrameCommitted -= subscriber.Frame;
            owner.MeasurementCompleted -= subscriber.Measurement;
            owner.MeasurementChanged -= subscriber.Measurement;
            owner.MeasurementRemoved -= subscriber.Measurement;
        }
        if (owner is Viewer window)
        {
            Assert.Throws<ObjectDisposedException>(() => window.Closed += subscriber.Closed);
            window.Closed -= subscriber.Closed;
            window.Closed -= subscriber.Closed;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(retained.IsAlive);
        GC.KeepAlive(owner);
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> RaceSubscriptionWithDisposal(IViewer owner)
    {
        var subscriber = new Subscriber();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action[] adds =
        [
            () => owner.FrameCommitted += subscriber.Frame,
            () => owner.MeasurementCompleted += subscriber.Measurement,
            () => owner.MeasurementChanged += subscriber.Measurement,
            () => owner.MeasurementRemoved += subscriber.Measurement,
            () => { if (owner is Viewer window) window.Closed += subscriber.Closed; }
        ];
        // Include accepted subscribers even if disposal wins every racing add.
        foreach (var add in adds) add();
        var subscribing = adds.Select(add => Task.Run(async () =>
        {
            await ready.Task;
            for (int i = 0; i < 20; i++)
                try { add(); } catch (ObjectDisposedException) { }
        })).ToArray();
        var disposing = Task.Run(async () => { await ready.Task; await owner.DisposeAsync(); });
        ready.SetResult();
        await Task.WhenAll(subscribing.Append(disposing)).WaitAsync(TimeSpan.FromSeconds(10));
        return new WeakReference(subscriber);
    }

    private sealed class Subscriber
    {
        internal void Frame(object? sender, FrameInfo info) { }
        internal void Measurement(object? sender, MeasurementEventArgs args) { }
        internal void Closed(object? sender, EventArgs args) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MeasurementSubscriptionsKeepQueuedSnapshotsAndPublicSenders(bool standalone) => ControlTestDispatcher.Run(async () =>
    {
        await using IViewer owner = standalone ? new Viewer(showWindow: false) : new ImageViewerControl();
        var control = owner is Viewer window ? window.Host.Control : (ImageViewerControl)owner;
        await control.Dispatcher.InvokeAsync(() =>
        {
            MeasurementItem Create() => (MeasurementItem)new MeasurementCreationContext(
                control.Runtime.Measurements, control.Runtime.MeasurementRuntime, owner.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Point(new()));
            CheckSnapshot<MeasurementEventArgs>(h => owner.MeasurementCompleted += h, h => owner.MeasurementCompleted -= h,
                () => Create().Complete(), owner);
            CheckSnapshot<MeasurementEventArgs>(h => owner.MeasurementRemoved += h, h => owner.MeasurementRemoved -= h,
                () => { var removed = Create(); removed.Complete(); removed.Dispose(); }, owner);
            var item = Create();
            item.Complete();
            IMeasurement handle = item;
            int x = 0;
            CheckSnapshot<MeasurementEventArgs>(h => owner.MeasurementChanged += h, h => owner.MeasurementChanged -= h,
                () => handle.UpdateGeometry(MeasurementGeometry.Point(new(++x, 0))), owner);
            CheckSnapshot<MeasurementGeometry>(h => handle.GeometryChanged += h, h => handle.GeometryChanged -= h,
                () => handle.UpdateGeometry(MeasurementGeometry.Point(new(++x, 0))), handle);
            CheckSnapshot<MeasurementQueryResult?>(h => handle.QueryResultChanged += h, h => handle.QueryResultChanged -= h,
                () => item.PublishQueryResult(new MeasurementSampleResult(item.Id, item.GeometryVersion,
                    new(1, new(1, 1, 1, FramePixelFormat.Gray8), null), MeasurementQueryKind.Pixel,
                    [new(0, 0)], [new(FramePixelFormat.Gray8, 7, 0, 0, 0, 255)])), handle);
        });
    });

    private static void CheckSnapshot<T>(Action<EventHandler<T>> add, Action<EventHandler<T>> remove, Action fire, object expectedSender)
    {
        var order = new List<string>();
        var senders = new List<object?>();
        bool changed = false;
        EventHandler<T> late = (_, _) => order.Add("late");
        EventHandler<T> second = (sender, _) => { senders.Add(sender); order.Add("second"); };
        EventHandler<T> duplicate = (_, _) => order.Add("duplicate");
        EventHandler<T> first = (_, _) =>
        {
            order.Add("first");
            if (changed) return;
            changed = true;
            remove(second);
            add(late);
        };
        EventHandler<T> failing = (_, _) => throw new InvalidOperationException("subscriber");
        add(first); add(second); add(failing); add(duplicate); add(duplicate); remove(duplicate);
        try
        {
            fire();
            Assert.Equal(new[] { "first", "second", "duplicate" }, order);
            Assert.Same(expectedSender, Assert.Single(senders));
            order.Clear();
            fire();
            Assert.Equal(new[] { "first", "duplicate", "late" }, order);
        }
        finally { remove(first); remove(second); remove(failing); remove(duplicate); remove(late); }
    }

    [Fact]
    public async Task ClosedKeepsCapturedSubscribersAndRejectsReentrantSubscription()
    {
        await using var viewer = new Viewer(showWindow: false);
        var order = new List<string>();
        object? senderSeen = null;
        Exception? rejected = null;
        EventHandler second = (sender, _) => { senderSeen = sender; order.Add("second"); };
        viewer.Closed += (_, _) =>
        {
            order.Add("first");
            viewer.Closed -= second;
            rejected = Record.Exception(() => viewer.Closed += second);
        };
        viewer.Closed += (_, _) => throw new InvalidOperationException("subscriber");
        viewer.Closed += second;
        viewer.Closed += second;
        viewer.Closed -= second;
        await viewer.DisposeAsync();
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Same(viewer, senderSeen);
        Assert.IsType<ObjectDisposedException>(rejected);
    }

    private static async Task Submit(IFrameSink sink)
    {
        var result = await sink.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
        Assert.Equal(FrameSubmitStatus.Committed, result.Status);
    }
}
