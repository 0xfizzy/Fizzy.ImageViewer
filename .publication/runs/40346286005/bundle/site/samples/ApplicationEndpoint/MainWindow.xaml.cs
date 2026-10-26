using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;

namespace ApplicationEndpoint;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _stop = new();
    private ImageViewerEndpoint? _endpoint;
    private DisplayViewModel? _viewModel;
    private SyntheticProducer? _producer;
    private Task? _production;
    private Task<List<Exception>>? _cleanup;
    private Action? _finish;
    private bool _closing, _closeReady;

    internal void Initialize(ServiceProvider provider, Action finish)
    {
        InitializeComponent();
        _finish = finish;
        _endpoint = provider.GetRequiredService<ImageViewerEndpoint>();
        _endpoint.Attach(ImageViewer);
        _viewModel = provider.GetRequiredService<DisplayViewModel>();
        DataContext = _viewModel;
        _producer = provider.GetRequiredService<SyntheticProducer>();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_production is null && !_closing && _producer is not null)
            _production = ObserveProducerAsync(_producer);
    }

    private async Task ObserveProducerAsync(SyntheticProducer producer)
    {
        try { await Task.Run(() => producer.RunAsync(_stop.Token)); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (DisplaySessionStoppedException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { _viewModel?.ReportError(error); throw; }
    }

    internal Task<List<Exception>> CleanupAsync() => _cleanup ??= CleanupCoreAsync();

    private async Task<List<Exception>> CleanupCoreAsync()
    {
        List<Exception> errors = [];
        _viewModel?.Stop();
        _endpoint?.StopAccepting();
        _stop.Cancel();
        try { if (_production is not null) await _production; }
        catch (Exception error) { errors.Add(error); }
        _viewModel?.Detach();
        try { _endpoint?.Detach(); }
        catch (Exception error) { errors.Add(error); }
        try { if (ImageViewer is not null) await ImageViewer.DisposeAsync(); }
        catch (Exception error) { errors.Add(error); }
        _stop.Dispose();
        return errors;
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_closeReady) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        var errors = await CleanupAsync();
        if (errors.Count != 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(x => x.Message)), "Shutdown failed");
        _closeReady = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            try { _finish?.Invoke(); }
            catch (Exception error) { errors.Add(error); MessageBox.Show(error.Message, "Application cleanup failed"); }
            finally { Close(); Application.Current?.Shutdown(errors.Count == 0 ? 0 : 1); }
        });
    }
}
