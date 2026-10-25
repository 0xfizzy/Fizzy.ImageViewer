using System.Windows.Input;

namespace ApplicationEndpoint;

internal sealed class DisplayCommand(Action execute) : ICommand
{
    private bool _enabled = true;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _enabled;
    public void Execute(object? parameter) { if (_enabled) execute(); }
    public void Stop()
    {
        _enabled = false;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
