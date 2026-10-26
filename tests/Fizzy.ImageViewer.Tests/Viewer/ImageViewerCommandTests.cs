using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class ImageViewerCommandTests
{
    [Fact]
    public Task ExplicitTargetsParametersAndAvailabilityUseActualControlState() => ControlTestDispatcher.Run(async () =>
    {
        await using var first = new ImageViewerControl { Height = 150 };
        await using var second = new ImageViewerControl { Height = 150 };
        var activate = ImageViewerCommands.ActivateMeasurementTool;
        var end = ImageViewerCommands.EndInteraction;
        var fit = ImageViewerCommands.FitToViewport;
        var tool = new Tool();
        using var registration = first.RegisterMeasurementTool(tool);
        Assert.False(activate.CanExecute(tool.Id, first));
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        var button = new Button { Command = activate, CommandParameter = tool.Id, CommandTarget = first };
        panel.Children.Add(button);
        var endButton = new Button { Command = end, CommandTarget = first };
        panel.Children.Add(endButton);
        var window = new Window { Content = panel, Width = 400, Height = 450, ShowActivated = false };
        try
        {
            window.Show(); await Pump();
            Assert.True(button.IsEnabled);
            foreach (var invalid in new object?[] { null, "", " ", "unknown", 42 })
            {
                Assert.False(activate.CanExecute(invalid, first));
                activate.Execute(invalid, first);
            }
            for (int i = 0; i < 5; i++) Assert.True(activate.CanExecute(tool.Id, first));
            Assert.Equal(0, tool.Created);
            Assert.False(activate.CanExecute(tool.Id, second));
            Assert.False(fit.CanExecute(null, first));
            Assert.False(end.CanExecute(null, first));
            Assert.False(endButton.IsEnabled);
            activate.Execute(tool.Id, first);
            await Pump(); Assert.True(endButton.IsEnabled);
            Assert.Equal(1, tool.Created);
            Assert.Equal(tool.Id, first.Runtime.Interaction.ActiveId);
            Assert.Null(second.Runtime.Interaction.ActiveId);
            Assert.True(end.CanExecute(null, first));
            Assert.False(end.CanExecute(null, second));
            end.Execute(null, first);
            await Pump(); Assert.False(endButton.IsEnabled);
            Assert.Equal(1, tool.Disposed);
            Assert.False(end.CanExecute(null, first));
            activate.Execute(MeasurementToolIds.Length, second);
            Assert.Equal(MeasurementToolIds.Length, second.Runtime.Interaction.ActiveId);

            await first.SubmitAndDisplayAsync(ImageFrame.Copy(new(2, 2, 2, FramePixelFormat.Gray8), new byte[4]));
            Assert.True(fit.CanExecute(null, first));
            first.ImageViewport.Scaler.ScaleX = 1;
            fit.Execute(null, first);
            Assert.NotEqual(1, first.ImageViewport.Scaler.ScaleX);

            first.Measurements.IsHitTestVisible = false; await Pump();
            Assert.False(button.IsEnabled);
            first.Measurements.IsHitTestVisible = true; await Pump(); Assert.True(button.IsEnabled);
            first.Measurements.IsVisible = false; await Pump(); Assert.False(button.IsEnabled);
            first.Measurements.IsVisible = true; await Pump(); Assert.True(button.IsEnabled);
            first.IsEnabled = false; Assert.False(activate.CanExecute(tool.Id, first));
            first.IsEnabled = true;
            first.Visibility = Visibility.Hidden; Assert.False(fit.CanExecute(null, first));
            first.Visibility = Visibility.Visible;
            panel.Children.Remove(first); await Pump(); Assert.False(button.IsEnabled);
            panel.Children.Insert(0, first); await Pump(); Assert.True(button.IsEnabled);
            registration.Dispose(); await Pump(); Assert.False(button.IsEnabled);
            await first.DisposeAsync();
            Assert.False(fit.CanExecute(null, first));
            Assert.False(activate.CanExecute(MeasurementToolIds.Point, first));
            fit.Execute(null, first); end.Execute(null, first); activate.Execute(tool.Id, first);
            Assert.True(end.CanExecute(null, second));
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); window.Close(); }
    });

    [Fact]
    public Task CommandMenuAndEscapeShareInteractionAndExecutionFailurePropagates() => ControlTestDispatcher.Run(async () =>
    {
        await using var control = new ImageViewerControl();
        var window = new Window { Content = control, Width = 250, Height = 250, ShowActivated = false };
        try
        {
            window.Show(); await Pump();
            var tool = new Tool();
            using var registration = control.RegisterMeasurementTool(tool);
            var menu = new System.Windows.Controls.MenuItem
            {
                Command = ImageViewerCommands.ActivateMeasurementTool,
                CommandParameter = tool.Id, CommandTarget = control
            };
            var popup = new ContextMenu { PlacementTarget = control };
            popup.Items.Add(menu);
            popup.IsOpen = true;
            await Pump();
            Assert.True(menu.IsEnabled);
            popup.IsOpen = false;
            // Execute the same routed command a menu or KeyBinding invokes.
            control.InputBindings.Add(new KeyBinding(ImageViewerCommands.ActivateMeasurementTool,
                new KeyGesture(Key.M, ModifierKeys.Control)) { CommandParameter = tool.Id, CommandTarget = control });
            var key = (KeyBinding)control.InputBindings[0];
            ((RoutedCommand)key.Command).Execute(key.CommandParameter, key.CommandTarget);
            Assert.Equal(1, tool.Created);
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            control.ImageViewport.Container.RaiseEvent(escape);
            Assert.True(escape.Handled); Assert.Equal(1, tool.Disposed);
            Assert.False(ImageViewerCommands.EndInteraction.CanExecute(null, control));
            var error = new InvalidOperationException("factory");
            tool.Error = error;
            Assert.True(ImageViewerCommands.ActivateMeasurementTool.CanExecute(tool.Id, control));
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => ImageViewerCommands.ActivateMeasurementTool.Execute(tool.Id, control)));
            Assert.Null(control.Runtime.Interaction.ActiveId);
        }
        finally { await control.DisposeAsync(); window.Close(); }
    });

    private static async Task Pump() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class Tool : IMeasurementTool
    {
        internal int Created, Disposed;
        internal Exception? Error;
        public string Id => "custom";
        public string DisplayName => "Custom";
        public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        {
            Created++;
            if (Error != null) throw Error;
            return new TestMeasurementSession(_ => false, _ => { }, () => { }, () => Disposed++);
        }
    }
}
