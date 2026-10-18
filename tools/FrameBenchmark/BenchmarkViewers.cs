using Fizzy.ImageViewer;
using System.Windows.Threading;

internal sealed class BenchmarkViewers : IAsyncDisposable
{
    internal IViewer[] Viewers { get; private set; } = [];
    private Thread? _thread;
    private Dispatcher? _dispatcher;

    internal static async Task<BenchmarkViewers> CreateAsync(int count, bool embedded)
    {
        var host = new BenchmarkViewers();
        if (!embedded)
        {
            host.Viewers = Enumerable.Range(0, count).Select(_ => (IViewer)new Viewer(showWindow: false)).ToArray();
            return host;
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host._thread = new Thread(() =>
        {
            try
            {
                host._dispatcher = Dispatcher.CurrentDispatcher;
                host.Viewers = Enumerable.Range(0, count).Select(_ => (IViewer)new ImageViewerControl()).ToArray();
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

    public async ValueTask DisposeAsync()
    {
        try { foreach (var viewer in Viewers) await viewer.DisposeAsync(); }
        finally
        {
            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
            if (_thread != null) await Task.Run(_thread.Join);
        }
    }
}
