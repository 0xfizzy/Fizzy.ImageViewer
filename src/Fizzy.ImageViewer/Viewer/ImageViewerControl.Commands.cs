using Fizzy.ImageViewer.Interaction;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    private void InitializeViewerCommands()
    {
        CommandBindings.Add(new CommandBinding(ImageViewerCommands.FitToViewport, ExecuteViewerCommand, CanExecuteViewerCommand));
        CommandBindings.Add(new CommandBinding(ImageViewerCommands.ActivateMeasurementTool, ExecuteViewerCommand, CanExecuteViewerCommand));
        CommandBindings.Add(new CommandBinding(ImageViewerCommands.EndInteraction, ExecuteViewerCommand, CanExecuteViewerCommand));
        _runtime.Interaction.StateChanged += CommandManager.InvalidateRequerySuggested;
        _runtime.Tools.Changed += CommandManager.InvalidateRequerySuggested;
        Measurements.InputPolicyChanged += CommandManager.InvalidateRequerySuggested;
        IsEnabledChanged += CommandAvailabilityChanged;
        SizeChanged += CommandSizeChanged;
    }

    internal void DetachViewerCommands()
    {
        if (_runtime.Interaction != null) _runtime.Interaction.StateChanged -= CommandManager.InvalidateRequerySuggested;
        if (_runtime.Tools != null) _runtime.Tools.Changed -= CommandManager.InvalidateRequerySuggested;
        if (_layers != null) Measurements.InputPolicyChanged -= CommandManager.InvalidateRequerySuggested;
        IsEnabledChanged -= CommandAvailabilityChanged;
        SizeChanged -= CommandSizeChanged;
        // Keep the bindings as terminal handlers so routing cannot fall through to another viewer.
        CommandManager.InvalidateRequerySuggested();
    }

    private void CommandAvailabilityChanged(object sender, DependencyPropertyChangedEventArgs args) => CommandManager.InvalidateRequerySuggested();
    private void CommandSizeChanged(object sender, SizeChangedEventArgs args) => CommandManager.InvalidateRequerySuggested();

    private bool CanExecuteViewerCommand(ICommand command, object? parameter)
    {
        // Background disposal may close admission while WPF queries command state on the STA.
        lock (_runtime.Lifetime.Gate)
        {
            if (_runtime.Lifetime.IsStopping || !IsLoaded || !IsVisible || !IsEnabled || IsMinimized) return false;
            if (command == ImageViewerCommands.FitToViewport)
                return ImageViewport.ImageDisplay.Source != null && ImageViewport.Container.ActualWidth > 0 && ImageViewport.Container.ActualHeight > 0;
            if (command == ImageViewerCommands.ActivateMeasurementTool)
                return parameter is string id && !string.IsNullOrWhiteSpace(id) && _runtime.Interaction.CanActivateMeasurementTool(id);
            return command == ImageViewerCommands.EndInteraction && _runtime.Interaction.Mode != InteractionMode.Idle;
        }
    }

    private void CanExecuteViewerCommand(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = CanExecuteViewerCommand(args.Command, args.Parameter);
        args.Handled = true;
    }

    private void ExecuteViewerCommand(object sender, ExecutedRoutedEventArgs args)
    {
        args.Handled = true;
        bool started = false;
        try
        {
            InvokeAlive(() =>
            {
                if (!CanExecuteViewerCommand(args.Command, args.Parameter)) return;
                started = true;
                if (args.Command == ImageViewerCommands.FitToViewport) ImageViewport.FitToViewport();
                else if (args.Command == ImageViewerCommands.ActivateMeasurementTool)
                    _runtime.Interaction.ActivateMeasurementTool((string)args.Parameter);
                else _runtime.Interaction.Cancel();
            });
        }
        catch (ObjectDisposedException) when (!started && _runtime.Lifetime.IsStopping) { }
    }
}
