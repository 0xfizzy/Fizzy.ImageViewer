using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace Fizzy.ImageViewer.Menus;

/// <summary>Owns registrations and WPF bindings; menu policy belongs to its subscribers.</summary>
internal sealed class MenuManager : IDisposable
{
    private readonly FrameworkElement _target;
    private readonly ContextMenu _menu = new();
    private readonly ViewerLifetime _lifetime;
    private readonly List<Registration> _registrations = [];
    private readonly List<ClickBinding> _bindings = [];
    private bool _disposed, _open;
    private long _generation;
    internal event Action? Opening;
    internal event Action? Closing;

    internal MenuManager(FrameworkElement target, ViewerLifetime? lifetime = null)
    {
        _target = target;
        _lifetime = lifetime ?? new ViewerLifetime();
        target.ContextMenu = _menu;
        _menu.Opened += Opened;
        _menu.Closed += Closed;
    }

    internal IDisposable Register(IMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Register(() => [item]);
    }

    internal IDisposable Register(Func<IEnumerable<IMenuItem>> provider)
    {
        _menu.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lifetime.ThrowIfStopping();
        ArgumentNullException.ThrowIfNull(provider);
        var registration = new Registration(this, provider);
        _registrations.Add(registration);
        return registration;
    }

    private sealed class Registration(MenuManager owner, Func<IEnumerable<IMenuItem>> provider) : IDisposable
    {
        private MenuManager? _owner = owner;
        private Func<IEnumerable<IMenuItem>>? _provider = provider;
        internal bool IsActive => Volatile.Read(ref _owner) != null;
        internal IMenuItem[] Capture() => Volatile.Read(ref _provider)?.Invoke().ToArray() ?? [];
        internal void Invalidate()
        {
            Interlocked.Exchange(ref _owner, null);
            Interlocked.Exchange(ref _provider, null);
        }
        public void Dispose()
        {
            var manager = Interlocked.Exchange(ref _owner, null);
            Interlocked.Exchange(ref _provider, null);
            manager?.Remove(this);
        }
    }

    private sealed class ClickBinding(MenuManager manager, Registration registration, WpfMenuItem visual, IMenuItem item) : IDisposable
    {
        private IMenuItem? _item = item;
        internal Registration Registration { get; } = registration;
        internal void Attach() => visual.Click += Execute;
        private void Execute(object sender, RoutedEventArgs args)
        {
            if (!manager._disposed && !manager._lifetime.IsStopping && Registration.IsActive)
                _item?.Execute(sender, args);
        }
        public void Dispose()
        {
            visual.Click -= Execute;
            visual.IsEnabled = false;
            _item = null;
        }
    }

    private void Remove(Registration registration)
    {
        // Shutdown owns the remaining STA cleanup. The registration is already inactive.
        if (_lifetime.IsStopping) return;
        try
        {
            _menu.Dispatcher.Invoke(() =>
            {
                _registrations.Remove(registration);
                foreach (var binding in _bindings.Where(b => b.Registration == registration).ToArray())
                {
                    binding.Dispose();
                    _bindings.Remove(binding);
                }
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsStopping || _menu.Dispatcher.HasShutdownStarted) { }
        catch (InvalidOperationException) when (_lifetime.IsStopping || _menu.Dispatcher.HasShutdownStarted) { }
    }

    private bool IsCurrent(long generation) => !_disposed && !_lifetime.IsStopping && _open && generation == _generation;

    private void Opened(object sender, RoutedEventArgs args)
    {
        if (_disposed || _lifetime.IsStopping) return;
        var generation = ++_generation;
        ClearBindings();
        _open = true;
        try
        {
            Opening?.Invoke();
            if (!IsCurrent(generation)) return;
            bool separator = false;
            int count = 0;
            // Callbacks may register/unregister items. New registrations appear next time.
            foreach (var registration in _registrations.ToArray())
            {
                if (!IsCurrent(generation)) return;
                if (!registration.IsActive) continue;
                foreach (var item in registration.Capture())
                {
                    if (!IsCurrent(generation)) return;
                    if (!registration.IsActive) break;
                    if (item is SeparatorMenuItem) { separator = true; continue; }
                    var visible = item.IsVisible;
                    if (!IsCurrent(generation)) return;
                    if (!registration.IsActive) break;
                    if (!visible) continue;
                    var header = item.Header;
                    var checkable = item is ICheckableMenuItem;
                    var isChecked = item is ICheckableMenuItem check && check.IsChecked;
                    if (!IsCurrent(generation)) return;
                    if (!registration.IsActive) break;
                    if (separator && count > 0) _menu.Items.Add(new Separator());
                    separator = false;
                    var visual = new WpfMenuItem { Header = header, IsCheckable = checkable, IsChecked = isChecked };
                    var binding = new ClickBinding(this, registration, visual, item);
                    binding.Attach();
                    _bindings.Add(binding);
                    _menu.Items.Add(visual);
                    count++;
                }
            }
        }
        catch
        {
            if (generation == _generation && !_disposed)
            {
                try { EndOpening(); }
                finally { ClearBindings(); }
            }
            throw;
        }
    }

    private void Closed(object sender, RoutedEventArgs args)
    {
        if (_disposed || !_open) return;
        var generation = _generation;
        try { EndOpening(); }
        finally
        {
            // Closed can precede Click. Reopening invalidates this delayed cleanup.
            _menu.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed && !_open && generation == _generation) ClearBindings();
            }, DispatcherPriority.ContextIdle);
        }
    }

    private void EndOpening()
    {
        if (!_open) return;
        _open = false;
        Closing?.Invoke();
    }

    private void ClearBindings()
    {
        foreach (var binding in _bindings) binding.Dispose();
        _bindings.Clear();
        _menu.Items.Clear();
    }

    public void Dispose()
    {
        _menu.Dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _generation++;
        foreach (var registration in _registrations) registration.Invalidate();
        _registrations.Clear();
        _menu.Opened -= Opened;
        _menu.Closed -= Closed;
        try { EndOpening(); }
        finally
        {
            Opening = Closing = null;
            ClearBindings();
            _menu.IsOpen = false;
            if (ReferenceEquals(_target.ContextMenu, _menu)) _target.ContextMenu = null;
        }
    }
}
