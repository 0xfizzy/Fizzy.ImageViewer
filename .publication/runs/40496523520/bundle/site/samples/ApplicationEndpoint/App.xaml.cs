using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace ApplicationEndpoint;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var provider = BuildServices();
        MainWindow? window = null;
        try
        {
            // Keep a reference before XAML initialization so partial startup can be cleaned up.
            window = new MainWindow();
            window.Initialize(provider, provider.Dispose);
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            if (window is not null) errors.AddRange(await window.CleanupAsync());
            try { provider.Dispose(); } catch (Exception cleanupError) { errors.Add(cleanupError); }
            MessageBox.Show(string.Join(Environment.NewLine, errors.Select(x => x.Message)), "Startup failed");
            Shutdown(1);
        }
    }

    internal static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ImageViewerEndpoint>();
        services.AddSingleton<IImageSink>(sp => sp.GetRequiredService<ImageViewerEndpoint>());
        services.AddSingleton<IImageDisplay>(sp => sp.GetRequiredService<ImageViewerEndpoint>());
        services.AddSingleton<IImageNotifications>(sp => sp.GetRequiredService<ImageViewerEndpoint>());
        services.AddSingleton<SyntheticProducer>();
        services.AddSingleton<DisplayViewModel>();
        return services.BuildServiceProvider();
    }
}
