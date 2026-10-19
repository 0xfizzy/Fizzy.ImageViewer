using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using System.ComponentModel;
using System.Windows;

namespace EmbeddedViewer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _producer;
    private bool _closing, _closeReady;

    public MainWindow() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_producer != null) return;
        ImageViewer.HudLabelText = "Synthetic live image";
        ImageViewer.MeasurementCompleted += (_, e) => Status.Text = $"Measurement completed: {e.Snapshot.Geometry}";
        ImageViewer.DisplayFailed += (_, failure) => Status.Text = $"Display failed: {failure.Error.Message}";
        _producer = StreamAsync();
    }

    private async Task StreamAsync()
    {
        try
        {
            const int width = 640, height = 480;
            var pixels = new byte[width * height];
            int phase = 0;
            while (!_stop.IsCancellationRequested)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) pixels[y * width + x] = (byte)((x + y + phase) % 256);
                var result = await ImageViewer.SubmitFrameAsync(ImageFrame.Copy(
                    new(width, height, width, FramePixelFormat.Gray8), pixels), cancellationToken: _stop.Token);
                if (result.Error is { } error) throw error;
                if (result.Status == FrameSubmitStatus.Closed) break;
                phase++;
                await Task.Delay(33, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { Status.Text = $"Stream failed: {error.Message}"; throw; }
    }

    private void FitImage(object sender, RoutedEventArgs args) => ImageViewer.FitToViewport();
    private void MeasureLength(object sender, RoutedEventArgs args) => ImageViewer.ActivateMeasurementTool(MeasurementToolIds.Length);
    private void EndInteraction(object sender, RoutedEventArgs args) => ImageViewer.EndInteraction();

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_closeReady) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        List<Exception> errors = [];
        _stop.Cancel();
        try { if (_producer != null) await _producer; }
        catch (Exception error) { errors.Add(error); }
        try { await ImageViewer.DisposeAsync(); }
        catch (Exception error) { errors.Add(error); }
        _stop.Dispose();
        if (errors.Count != 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(e => e.Message)), "Shutdown failed");
        _closeReady = true;
        // Leave the current Closing callback before requesting final closure.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
