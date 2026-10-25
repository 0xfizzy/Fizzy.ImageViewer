using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ControlInputFocusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClickingBackIntoAControlRoutesEscapeToItsOwnSession(bool drawing) => ControlTestDispatcher.Run(async () =>
    {
        await using var first = new ImageViewerControl { Width = 100, Height = 100 };
        await using var second = new ImageViewerControl { Width = 100, Height = 100 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(first);
        panel.Children.Add(second);
        var window = new Window { Content = panel, ShowActivated = false, Width = 300, Height = 200 };
        try
        {
            window.Show();
            first.ActivateMeasurementTool(MeasurementToolIds.Length);
            second.ActivateMeasurementTool(MeasurementToolIds.Length);
            Assert.Same(second.ImageViewport.Container, FocusManager.GetFocusedElement(window));

            // Routed input at the actual viewport surface, after focus moved to its sibling.
            var surface = drawing ? (UIElement)first.Markers.Host : first.ImageViewport.Container;
            surface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Assert.Same(first.ImageViewport.Container, FocusManager.GetFocusedElement(window));
            var focused = Assert.IsAssignableFrom<UIElement>(FocusManager.GetFocusedElement(window));
            focused.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.Null(first.Runtime.Interaction.ActiveId);
            Assert.Equal(MeasurementToolIds.Length, second.Runtime.Interaction.ActiveId);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task MeasurementSelectionKeepsOverlayFocusAndDeleteIsLocal() => ControlTestDispatcher.Run(async () =>
    {
        await using var first = new ImageViewerControl { Width = 100, Height = 100 };
        await using var second = new ImageViewerControl { Width = 100, Height = 100 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(first);
        panel.Children.Add(second);
        var window = new Window { Content = panel, ShowActivated = false, Width = 300, Height = 200 };
        try
        {
            window.Show();
            foreach (var control in new[] { first, second })
            {
                control.ActivateMeasurementTool(MeasurementToolIds.Length);
                control.Runtime.Interaction.ImageDown(10, 10);
                control.Runtime.Interaction.ImageDown(80, 10);
            }
            var line = first.MeasurementOverlay.Canvas.Children.OfType<System.Windows.Shapes.Line>().Single();
            line.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
            line.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.MouseDownEvent });
            Assert.NotNull(first.Runtime.Interaction.SelectedMeasurement);
            Assert.Same(first.MeasurementOverlay.Canvas, FocusManager.GetFocusedElement(window));
            first.MeasurementOverlay.Canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first), 0, Key.Delete)
            { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Empty(first.MeasurementOverlay.Canvas.Children);
            Assert.NotEmpty(second.MeasurementOverlay.Canvas.Children);
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });
}
