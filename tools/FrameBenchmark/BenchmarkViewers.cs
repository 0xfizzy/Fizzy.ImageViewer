using Fizzy.ImageViewer;
using System.Windows.Threading;
using System.Windows;

internal sealed class BenchmarkViewers : IAsyncDisposable
{
    internal IViewer[] Viewers { get; private set; } = [];
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private Window[] _windows = [];

    internal static async Task<BenchmarkViewers> CreateAsync(int count, bool embedded)
    {
        var host = new BenchmarkViewers();
        if (!embedded)
        {
            host.Viewers = Enumerable.Range(0, count).Select(_ => (IViewer)new Viewer(showWindow: true)).ToArray();
            return host;
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host._thread = new Thread(() =>
        {
            try
            {
                host._dispatcher = Dispatcher.CurrentDispatcher;
                host.Viewers = Enumerable.Range(0, count).Select(_ => (IViewer)new ImageViewerControl()).ToArray();
                host._windows = host.Viewers.Select(view => new Window { Content = view, Width = 640, Height = 480, ShowActivated = false }).ToArray();
                foreach (var window in host._windows) window.Show();
                ready.SetResult();
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true };
        host._thread.SetApartmentState(ApartmentState.STA);
        host._thread.Start();
        await ready.Task;
        return host;
    }

    internal async Task SetVisibleAsync(bool visible)
    {
        foreach (var viewer in Viewers)
            if (viewer is Viewer window) window.Visible = visible;
            else await ((ImageViewerControl)viewer).Dispatcher.InvokeAsync(() => ((ImageViewerControl)viewer).Visible = visible);
    }

    public async ValueTask DisposeAsync()
    {
        try { foreach (var viewer in Viewers) await viewer.DisposeAsync(); }
        finally
        {
            if (_dispatcher != null) await _dispatcher.InvokeAsync(() => { foreach (var window in _windows) window.Close(); });
            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
            if (_thread != null) await Task.Run(_thread.Join);
        }
    }
}
