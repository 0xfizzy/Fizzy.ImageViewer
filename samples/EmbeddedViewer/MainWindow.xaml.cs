using Fizzy.ImageViewer.Frames;
using System.ComponentModel;
using System.Windows;

namespace EmbeddedViewer;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ImageViewModel _model = new();
    private Task? _producer;
    private Task? _imageLoad;
    private bool _closing, _closeReady;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_producer != null) return;
        ImageViewer.HudLabelText = "Bound static image";
        LiveViewer.HudLabelText = "Explicit live submissions";
        ImageViewer.MeasurementCompleted += (_, e) => Status.Text = $"Measurement completed: {e.Snapshot.Geometry}";
        ImageViewer.DisplayFailed += (_, failure) => Status.Text = $"Display failed: {failure.Error.Message}";
        LiveViewer.DisplayFailed += (_, failure) => Status.Text = $"Live display failed: {failure.Error.Message}";
        OnNextImage(sender, args);
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
                var result = await LiveViewer.SubmitFrameAsync(ImageFrame.Copy(
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

    private async void OnNextImage(object sender, RoutedEventArgs args)
    {
        if (_closing || _imageLoad is { IsCompleted: false }) return;
        _imageLoad = _model.LoadNextAsync();
        try { await _imageLoad; }
        catch (Exception error) { Status.Text = $"Image update failed: {error.Message}"; }
    }

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
        try { if (_imageLoad != null) await _imageLoad; }
        catch (Exception error) { errors.Add(error); }
        // All updates have ended. Release the model's reference on the host UI thread;
        // each viewer still owns its independent pipeline references until disposal.
        try { _model.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        try { await ImageViewer.DisposeAsync(); }
        catch (Exception error) { errors.Add(error); }
        try { await LiveViewer.DisposeAsync(); }
        catch (Exception error) { errors.Add(error); }
        _stop.Dispose();
        if (errors.Count != 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(e => e.Message)), "Shutdown failed");
        _closeReady = true;
        // Leave the current Closing callback before requesting final closure.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
