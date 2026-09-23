using Fizzy.ImageViewer.Menus;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using ActionMenuItem = Fizzy.ImageViewer.Menus.MenuItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MenuTests
{
    private static void Open(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
    private static void Close(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));

    [Fact]
    public async Task VisibilityAndCheckStateAreEvaluatedOnEachOpeningWithNormalizedSeparators()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
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
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            viewer.StartMeasure(MeasureToolIds.Point); viewer.Interaction.ImageDown(1, 1);
            viewer.StartMeasure(MeasureToolIds.Point); viewer.Interaction.ImageDown(2, 2);
            var overlay = viewer.WindowForTests.MeasurementOverlay;
            var shapes = overlay.Canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
            var menu = viewer.WindowForTests.ContextMenu;
            viewer.Interaction.Select(shapes[0]); Open(menu);
            var firstDelete = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Delete"));
            Close(menu);
            viewer.Interaction.Select(shapes[1]);
            firstDelete.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.DoesNotContain(shapes[0], overlay.Canvas.Children.Cast<UIElement>());
            Assert.Contains(shapes[1], overlay.Canvas.Children.Cast<UIElement>());
            viewer.Interaction.Select(shapes[1]); Open(menu); Close(menu);
            menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Delete"))
                .RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Empty(overlay.Canvas.Children.Cast<UIElement>());
        });
    }

    [Fact]
    public async Task InteractionMenuReflectsMeasurementStateAtOpening()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.UiDispatcher.InvokeAsync(() =>
        {
            var menu = viewer.WindowForTests.ContextMenu;
            Open(menu);
            Assert.Contains(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            Close(menu);
            viewer.StartMeasure(MeasureToolIds.Length);
            Open(menu);
            Assert.DoesNotContain(menu.Items.OfType<WpfMenuItem>(), i => Equals(i.Header, "Point"));
            var cancel = menu.Items.OfType<WpfMenuItem>().Single(i => Equals(i.Header, "Cancel Measurement"));
            Close(menu); cancel.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));
            Assert.Equal(Interaction.InteractionMode.Idle, viewer.Interaction.Mode);
        });
    }
}
