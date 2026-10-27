using System.ComponentModel;

namespace ApplicationEndpoint;

internal sealed class DisplayViewModel : INotifyPropertyChanged
{
    private readonly IImageNotifications _notifications;
    private string _status = "Choose Measure length, then click two points in the image.";
    public string Status { get => _status; private set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    public DisplayCommand FitCommand { get; }
    public DisplayCommand MeasureCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public DisplayViewModel(IImageDisplay display, IImageNotifications notifications)
    {
        _notifications = notifications;
        FitCommand = new(() => Run(display.FitImage));
        MeasureCommand = new(() => Run(() => { display.StartLengthMeasurement(); Status = "Click two endpoints; Escape cancels."; }));
        notifications.LengthCompleted += OnLengthCompleted;
        notifications.DisplayFailed += OnDisplayFailed;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { ReportError(error); }
    }
    public void ReportError(Exception error) => Status = $"Failed: {error.Message}";
    public void Stop() { FitCommand.Stop(); MeasureCommand.Stop(); }
    private void OnLengthCompleted(object? sender, LengthResult result) => Status = $"Length: {result.Pixels:F2} pixels";
    private void OnDisplayFailed(object? sender, string message) => Status = $"Display failed: {message}";
    public void Detach()
    {
        _notifications.LengthCompleted -= OnLengthCompleted;
        _notifications.DisplayFailed -= OnDisplayFailed;
    }
}
