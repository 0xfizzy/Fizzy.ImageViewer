using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Fizzy.ImageViewer.Frames;
using Xunit;
using ActionMenuItem = Fizzy.ImageViewer.Menus.MenuItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MenuTests
{
    private static Viewer Create() => new(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
    private static void Open(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
    private static void Close(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));

    [Fact]
    public async Task RegistrationDisposalRevokesOnlyItsBindingAndDoesNotDisposeTheItem()
    {
        await using var viewer = Create();
        var item = new DisposableItem();
        var first = viewer.RegisterMenuItem(item);
        var second = viewer.RegisterMenuItem(item);
        WpfMenuItem[] visuals = [];
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Open(viewer.Host.Control.ContextMenu);
            visuals = viewer.Host.Control.ContextMenu.Items.OfType<WpfMenuItem>()
                .Where(i => Equals(i.Header, item.Header)).ToArray();
            Assert.Equal(2, visuals.Length);
        });
        await Task.Run(first.Dispose);
        first.Dispose();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.False(visuals[0].IsEnabled);
            visuals[0].RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            visuals[1].RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, item.Clicks);
            Close(viewer.Host.Control.ContextMenu);
            Open(viewer.Host.Control.ContextMenu);
            Assert.Single(viewer.Host.Control.ContextMenu.Items.OfType<WpfMenuItem>()
                .Where(i => Equals(i.Header, item.Header)));
        });
        await viewer.DisposeAsync();
        second.Dispose();
        second.Dispose();
        Assert.Equal(0, item.Disposals);
        Assert.Throws<ObjectDisposedException>(() => viewer.RegisterMenuItem(item));
    }

    [Fact]
    public async Task PixelInfoMenuAndPublicApiShareState()
    {
        await using var viewer = Create();
        viewer.IsPixelInfoEnabled = false;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            var item = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Pixel Info"));
            Assert.False(item.IsChecked);
            item.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.True(viewer.IsPixelInfoEnabled);
            Close(menu);
            Open(menu);
            Assert.True(menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Pixel Info")).IsChecked);
            Close(menu);
        });
    }

    private sealed class DisposableItem : IMenuItem, IDisposable
    {
        public string Header => "owned by caller";
        public int Clicks, Disposals;
        public ValueTask ExecuteAsync() { Clicks++; return ValueTask.CompletedTask; }
        public void Dispose() => Disposals++;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationReleasesCallbackTargetsDespiteRetainedHandleAndVisual(bool closeViewer)
    {
        await using var viewer = Create();
        var retained = await viewer.Host.Window.Dispatcher.InvokeAsync(() => RegisterCollectibleItem(viewer));
        if (closeViewer) await viewer.DisposeAsync();
        else await Task.Run(retained.Handle.Dispose);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(retained.Item.IsAlive);
        GC.KeepAlive(retained.Handle);
        GC.KeepAlive(retained.Visual);
        GC.KeepAlive(viewer);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (IDisposable Handle, WpfMenuItem Visual, WeakReference Item) RegisterCollectibleItem(Viewer viewer)
    {
        var item = new DisposableItem();
        var handle = viewer.RegisterMenuItem(item);
        var menu = viewer.Host.Control.ContextMenu;
        Open(menu);
        var visual = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, item.Header));
        return (handle, visual, new WeakReference(item));
    }

    [Fact]
    public async Task ClickCanUnregisterItselfAndRegisterItsReplacement()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            int calls = 0;
            IDisposable? registration = null;
            registration = viewer.RegisterMenuItem(new ActionMenuItem("once", () =>
            {
                calls++;
                registration!.Dispose();
                viewer.RegisterMenuItem(new ActionMenuItem("replacement", () => { }));
            }));
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            var once = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "once"));
            Close(menu);
            once.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            once.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, calls);
            Open(menu);
            Assert.DoesNotContain(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "once"));
            Assert.Contains(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "replacement"));
        });
    }

    [Fact]
    public async Task VisibilityCallbacksCanRevokeAndReplaceRegistrationsDuringOpening()
    {
        await using var viewer = Create();
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Grid();
            using var menus = new MenuManager(target);
            IDisposable? self = null, other = null;
            self = menus.Register(new ActionMenuItem("self", () => { }, () =>
            {
                self!.Dispose(); other!.Dispose();
                menus.Register(new ActionMenuItem("next", () => { }));
                return true;
            }));
            other = menus.Register(new ActionMenuItem("other", () => { }));
            Open(target.ContextMenu);
            Assert.Empty(target.ContextMenu.Items);
            Close(target.ContextMenu);
            Open(target.ContextMenu);
            Assert.Equal("next", Assert.IsType<WpfMenuItem>(Assert.Single(target.ContextMenu.Items.Cast<object>())).Header);
        });
    }

    [Fact]
    public async Task ClosedBindingsSurviveClickThenExpireWithoutClearingReopenedMenu()
    {
        await using var viewer = Create();
        WpfMenuItem? old = null;
        ContextMenu? menu = null;
        int calls = 0;
        viewer.RegisterMenuItem(new ActionMenuItem("test", () => calls++));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            old = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "test"));
            Close(menu);
            old.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, calls);
            Open(menu);
        });
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.NotEmpty(menu!.Items);
            old!.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, calls);
            Close(menu);
        });
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() => Assert.Empty(menu!.Items));
    }

    [Theory]
    [InlineData("Header")]
    [InlineData("IsVisible")]
    [InlineData("IsChecked")]
    public async Task FailedOpeningUnfreezesFramesDiscardsPartialBindingsAndAllowsReopening(string property)
    {
        var log = new OpeningLog();
        await using var viewer = new Viewer(log, new WriteableBitmapPresenter(), true);
        var item = new FailingStateItem(property);
        using var registration = viewer.RegisterMenuItem(item);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            Assert.Empty(menu.Items);
            Assert.False(menu.IsOpen);
            Assert.Contains(item.Failure, log.Errors);
        });
        var result = await viewer.SubmitAndDisplayAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
        Assert.Equal(FrameSubmitStatus.Accepted, result.Status);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            item.Fail = false;
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            var visual = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "stateful"));
            Assert.True(visual.IsChecked);
            visual.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, item.Calls);
            Close(menu);
        });
    }

    [Theory]
    [InlineData("Header")]
    [InlineData("IsVisible")]
    [InlineData("IsChecked")]
    public async Task ActualPopupClosesAfterStateCallbackFailure(string property)
    {
        var log = new OpeningLog();
        await using var viewer = new Viewer(log, new WriteableBitmapPresenter(), true);
        var item = new FailingStateItem(property);
        using var registration = viewer.RegisterMenuItem(item);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool observedOpen = false;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            // Create a test HWND without showing the viewer. Absolute placement and
            // StaysOpen avoid physical mouse input, capture and focus dependencies.
            new System.Windows.Interop.WindowInteropHelper(viewer.Host.Window).EnsureHandle();
            var menu = viewer.Host.Control.ContextMenu;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = menu.VerticalOffset = 0;
            menu.StaysOpen = true;
            viewer.Host.Control.Runtime.Menus.Opening += () => observedOpen = menu.IsOpen;
            menu.Closed += (_, _) => closed.TrySetResult();
            menu.IsOpen = true;
        });
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Assert.True(observedOpen);
            Assert.False(viewer.Host.Control.ContextMenu.IsOpen);
            Assert.Empty(viewer.Host.Control.ContextMenu.Items);
            Assert.Contains(item.Failure, log.Errors);
        }, DispatcherPriority.ApplicationIdle);
        var result = await viewer.SubmitAndDisplayAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
        Assert.Equal(FrameSubmitStatus.Accepted, result.Status);
    }

    private sealed class FailingStateItem(string property) : ICheckableMenuItem
    {
        public bool Fail = true;
        public int Calls;
        public Exception Failure { get; } = new InvalidOperationException("state callback failed");
        private void Check(string name) { if (Fail && property == name) throw Failure; }
        public string Header { get { Check(nameof(Header)); return "stateful"; } }
        public bool IsVisible { get { Check(nameof(IsVisible)); return true; } }
        public bool IsChecked { get { Check(nameof(IsChecked)); return true; } }
        public ValueTask ExecuteAsync() { Calls++; return ValueTask.CompletedTask; }
    }

    private sealed class OpeningLog : ILogger<Viewer>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { if (exception != null) Errors.Add(exception); }
    }

    [Fact]
    public async Task ManagerDisposalDetachesBindingsAndOnlyItsOwnContextMenu()
    {
        await using var viewer = Create();
        IDisposable? handle = null;
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Grid();
            var manager = new MenuManager(target);
            int calls = 0, closing = 0;
            manager.Closing += () => closing++;
            handle = manager.Register(new ActionMenuItem("test", () => calls++));
            var menu = target.ContextMenu;
            Open(menu);
            var visual = Assert.IsType<WpfMenuItem>(Assert.Single(menu.Items.Cast<object>()));
            var replacement = new ContextMenu();
            target.ContextMenu = replacement;
            manager.Dispose(); manager.Dispose();
            Assert.Same(replacement, target.ContextMenu);
            Assert.Equal(1, closing);
            Assert.Empty(menu.Items);
            visual.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Open(menu);
            Assert.Empty(menu.Items);
            Assert.Equal(0, calls);
        });
        await viewer.DisposeAsync();
        handle!.Dispose();
    }

    [Fact]
    public void StartupFailureRevokesMenuHandlesAndDetachesContextMenu()
    {
        IDisposable? handle = null;
        bool detached = false;
        var failure = new InvalidOperationException("initialization failed");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false, initialize: viewer =>
            {
                handle = viewer.RegisterMenuItem(new ActionMenuItem("test", () => { }));
                viewer.Host.Window.Closed += (_, _) => detached = viewer.Host.Control.ContextMenu == null;
                throw failure;
            })));
        Assert.True(detached);
        handle!.Dispose();
    }

    [Fact]
    public async Task VisibilityAndCheckStateAreEvaluatedOnEachOpeningWithNormalizedSeparators()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var target = new Grid();
            var manager = new MenuManager(target);
            bool visible = false, selected = false;
            int clicked = 0;
            manager.Register(SeparatorMenuItem.Instance);
            manager.Register(new ActionMenuItem("optional", () => clicked++, () => visible));
            manager.Register(SeparatorMenuItem.Instance);
            manager.Register(SeparatorMenuItem.Instance);
            manager.Register(new CheckableMenuItem("toggle", () => selected, () => selected = !selected));
            manager.Register(SeparatorMenuItem.Instance);
            Open(target.ContextMenu);
            var toggle = Assert.IsType<WpfMenuItem>(Assert.Single(target.ContextMenu.Items.Cast<object>()));
            Assert.False(toggle.IsChecked);
            toggle.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            visible = true;
            Close(target.ContextMenu); Open(target.ContextMenu);
            Assert.Equal(3, target.ContextMenu.Items.Count);
            Assert.IsType<Separator>(target.ContextMenu.Items[1]);
            Assert.True(((WpfMenuItem)target.ContextMenu.Items[2]).IsChecked);
            ((WpfMenuItem)target.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(1, clicked);
        });
    }

    [Fact]
    public async Task DeleteUsesOpeningTargetAndReopeningCapturesNewTarget()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.ActivateMeasurementTool(MeasurementToolIds.Point); viewer.Host.Control.Runtime.Interaction.ImageDown(1, 1);
            viewer.ActivateMeasurementTool(MeasurementToolIds.Point); viewer.Host.Control.Runtime.Interaction.ImageDown(2, 2);
            var overlay = viewer.Host.Control.MeasurementOverlay;
            var shapes = overlay.Canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
            var menu = viewer.Host.Control.ContextMenu;
            viewer.Host.Control.Runtime.Interaction.Select(viewer.Host.Control.Runtime.Measurements.Find(shapes[0])); Open(menu);
            var firstDelete = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Delete"));
            Close(menu);
            viewer.Host.Control.Runtime.Interaction.Select(viewer.Host.Control.Runtime.Measurements.Find(shapes[1]));
            firstDelete.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.DoesNotContain(shapes[0], overlay.Canvas.Children.Cast<UIElement>());
            Assert.Contains(shapes[1], overlay.Canvas.Children.Cast<UIElement>());
            viewer.Host.Control.Runtime.Interaction.Select(viewer.Host.Control.Runtime.Measurements.Find(shapes[1])); Open(menu); Close(menu);
            menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Delete"))
                .RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task RetainedMeasurementActionsIgnoreDisposedTarget()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var context = viewer.Host.Control.Runtime.Measurements;
            var first = (MeasurementItem)new MeasurementCreationContext(context, viewer.Host.Control.Runtime.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new(1, 1)));
            var second = (MeasurementItem)new MeasurementCreationContext(context, viewer.Host.Control.Runtime.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(MeasurementGeometry.Point(new(2, 2)));
            first.Complete(); second.Complete();
            viewer.Host.Control.Runtime.Interaction.Select(first);
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            var actions = menu.Items.OfType<WpfMenuItem>()
                .Where(i => Equals(i.Header, "Edit") || Equals(i.Header, "Delete")).ToArray();
            Assert.Equal(2, actions.Length);
            Close(menu);
            first.Dispose();
            viewer.Host.Control.Runtime.Interaction.Select(second);
            foreach (var action in actions) action.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.False(second.IsDisposed);
            Assert.Same(second, viewer.Host.Control.Runtime.Interaction.SelectedMeasurement);
            Assert.False(viewer.Host.Control.Runtime.Interaction.Editor.IsEditing);
        });
    }

    [Fact]
    public async Task RetainedCancelTargetsOnlyTheCapturedActivationEvenForTheSameTool()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            var cancel = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Cancel Measurement"));
            Close(menu);
            viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
            cancel.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(Interaction.InteractionMode.Measuring, viewer.Host.Control.Runtime.Interaction.Mode);
            Assert.Equal(MeasurementToolIds.Length, viewer.Host.Control.Runtime.Interaction.ActiveId);
            Open(menu);
            var currentCancel = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Cancel Measurement"));
            Close(menu);
            currentCancel.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(Interaction.InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
        });
    }

    [Fact]
    public async Task InteractionMenuReflectsMeasurementStateAtOpening()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Control.ContextMenu;
            Open(menu);
            Assert.Contains(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            Close(menu);
            viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
            Open(menu);
            Assert.DoesNotContain(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            var cancel = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Cancel Measurement"));
            Close(menu); cancel.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(Interaction.InteractionMode.Idle, viewer.Host.Control.Runtime.Interaction.Mode);
        });
    }
}
