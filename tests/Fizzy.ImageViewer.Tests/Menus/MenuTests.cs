using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
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
        var first = viewer.RegisterMenu(item);
        var second = viewer.RegisterMenu(item);
        WpfMenuItem[] visuals = [];
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            Open(viewer.Host.Window.ContextMenu);
            visuals = viewer.Host.Window.ContextMenu.Items.OfType<WpfMenuItem>()
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
            Close(viewer.Host.Window.ContextMenu);
            Open(viewer.Host.Window.ContextMenu);
            Assert.Single(viewer.Host.Window.ContextMenu.Items.OfType<WpfMenuItem>()
                .Where(i => Equals(i.Header, item.Header)));
        });
        await viewer.DisposeAsync();
        second.Dispose();
        second.Dispose();
        Assert.Equal(0, item.Disposals);
        Assert.Throws<ObjectDisposedException>(() => viewer.RegisterMenu(item));
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
        var handle = viewer.RegisterMenu(item);
        var menu = viewer.Host.Window.ContextMenu;
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
            registration = viewer.RegisterMenu(new ActionMenuItem("once", () =>
            {
                calls++;
                registration!.Dispose();
                viewer.RegisterMenu(new ActionMenuItem("replacement", () => { }));
            }));
            var menu = viewer.Host.Window.ContextMenu;
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
        viewer.RegisterMenu(new ActionMenuItem("test", () => calls++));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            menu = viewer.Host.Window.ContextMenu;
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

    [Fact]
    public async Task FailedOpeningUnfreezesFramesAndDiscardsPartialBindings()
    {
        await using var viewer = Create();
        var failure = new InvalidOperationException("visibility failed");
        using var registration = viewer.RegisterMenu(new ActionMenuItem("bad", () => { }, () => throw failure));
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Window.ContextMenu;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Open(menu)));
            Assert.Empty(menu.Items);
        });
        var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(new(1, 1, 1, FramePixelFormat.Gray8), new byte[] { 42 }));
        Assert.Equal(FrameSubmitStatus.Committed, result.Status);
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
                handle = viewer.RegisterMenu(new ActionMenuItem("test", () => { }));
                viewer.Host.Window.Closed += (_, _) => detached = viewer.Host.Window.ContextMenu == null;
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
            viewer.StartMeasurement(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(1, 1);
            viewer.StartMeasurement(MeasurementToolIds.Point); viewer.Host.Interaction.ImageDown(2, 2);
            var overlay = viewer.Host.Window.MeasurementOverlay;
            var shapes = overlay.Canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
            var menu = viewer.Host.Window.ContextMenu;
            viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(shapes[0])); Open(menu);
            var firstDelete = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Delete"));
            Close(menu);
            viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(shapes[1]));
            firstDelete.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.DoesNotContain(shapes[0], overlay.Canvas.Children.Cast<UIElement>());
            Assert.Contains(shapes[1], overlay.Canvas.Children.Cast<UIElement>());
            viewer.Host.Interaction.Select(viewer.Host.Measurements.Find(shapes[1])); Open(menu); Close(menu);
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
            var context = viewer.Host.Measurements;
            var first = (MeasurementItem)new MeasurementCreationSession(context).CreateMeasurement(MeasurementGeometry.Point(new(1, 1)));
            var second = (MeasurementItem)new MeasurementCreationSession(context).CreateMeasurement(MeasurementGeometry.Point(new(2, 2)));
            first.Complete(); second.Complete();
            viewer.Host.Interaction.Select(first);
            var menu = viewer.Host.Window.ContextMenu;
            Open(menu);
            var actions = menu.Items.OfType<WpfMenuItem>()
                .Where(i => Equals(i.Header, "Edit") || Equals(i.Header, "Delete")).ToArray();
            Assert.Equal(2, actions.Length);
            Close(menu);
            first.Dispose();
            viewer.Host.Interaction.Select(second);
            foreach (var action in actions) action.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.False(second.IsDisposed);
            Assert.Same(second, viewer.Host.Interaction.SelectedMeasurement);
            Assert.False(viewer.Host.Interaction.Editor.IsEditing);
        });
    }

    [Fact]
    public async Task InteractionMenuReflectsMeasurementStateAtOpening()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            var menu = viewer.Host.Window.ContextMenu;
            Open(menu);
            Assert.Contains(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            Close(menu);
            viewer.StartMeasurement(MeasurementToolIds.Length);
            Open(menu);
            Assert.DoesNotContain(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            var cancel = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Cancel Measurement"));
            Close(menu); cancel.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(Interaction.InteractionMode.Idle, viewer.Host.Interaction.Mode);
        });
    }
}
