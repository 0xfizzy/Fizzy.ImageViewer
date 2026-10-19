using Fizzy.ImageViewer.Interaction;
using Fizzy.ImageViewer.Measurements;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <inheritdoc/>
    public MeasurementLayer Measurements => Layers.Measurements;

    /// <inheritdoc/>
    public MeasurementStyle MeasurementStyle
    {
        get => InvokeAlive(() => _runtime.Measurements.Style);
        set
        {
            _runtime.Lifetime.ThrowIfStopping();
            ArgumentNullException.ThrowIfNull(value);
            var snapshot = value.Snapshot();
            InvokeAlive(() => _runtime.Measurements.Style = snapshot);
        }
    }

    /// <inheritdoc/>
    public IDisposable RegisterMeasurementTool(IMeasurementTool tool) => InvokeAlive(() =>
    {
        ArgumentNullException.ThrowIfNull(tool);
        var id = tool.Id;
        var displayName = tool.DisplayName;
        // Metadata getters are external callbacks and may pump shutdown on this STA.
        _runtime.Lifetime.ThrowIfStopping();
        var registration = _runtime.Tools.RegisterTool(tool, id, displayName);
        return registration.Handle = new MeasurementToolRegistration(() => _runtime.Lifetime.InvokeRemoval(Dispatcher,
            () => _runtime.Interaction.RevokeMeasurementTool(registration)));
    });

    /// <inheritdoc/>
    public bool UnregisterMeasurementTool(string toolId) => InvokeAlive(() =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        return _runtime.Interaction.UnregisterMeasurementTool(toolId);
    });

    /// <inheritdoc/>
    public void ActivateMeasurementTool(string toolId)
    {
        InvokeAlive(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
            var result = _runtime.Interaction.ActivateMeasurementTool(toolId);
            switch (result)
            {
                case MeasurementActivationResult.Unregistered:
                    throw new KeyNotFoundException($"Unknown measurement tool '{toolId}'.");
                case MeasurementActivationResult.Hidden:
                    throw new InvalidOperationException("Measurement layer is hidden.");
                case MeasurementActivationResult.InputDisabled:
                    throw new InvalidOperationException("Measurement layer hit testing is disabled.");
                case MeasurementActivationResult.Clearing:
                    throw new InvalidOperationException("Cannot activate a tool during layer cleanup.");
                case MeasurementActivationResult.Closed:
                    throw new ObjectDisposedException(nameof(Viewer));
            }
        });
    }

    /// <inheritdoc/>
    public void EndInteraction()
    {
        InvokeAlive(() => _runtime.Interaction.Cancel());
    }

    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementCompleted
    {
        add { lock (_runtime.Lifetime.Gate) { _runtime.Lifetime.ThrowIfStopping(); _measurementCompleted += value; } }
        remove { lock (_runtime.Lifetime.Gate) _measurementCompleted -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementCompleted;
    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementRemoved
    {
        add { lock (_runtime.Lifetime.Gate) { _runtime.Lifetime.ThrowIfStopping(); _measurementRemoved += value; } }
        remove { lock (_runtime.Lifetime.Gate) _measurementRemoved -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementRemoved;

    /// <inheritdoc />
    public event EventHandler<MeasurementEventArgs>? MeasurementChanged
    {
        add { lock (_runtime.Lifetime.Gate) { _runtime.Lifetime.ThrowIfStopping(); _measurementChanged += value; } }
        remove { lock (_runtime.Lifetime.Gate) _measurementChanged -= value; }
    }
    private EventHandler<MeasurementEventArgs>? _measurementChanged;

    internal void NotifyMeasurementChanged(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementChanged), args);
    internal void NotifyMeasurementCompleted(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementCompleted), args);
    internal void NotifyMeasurementRemoved(MeasurementEventArgs args) => NotifyMeasurement(Volatile.Read(ref _measurementRemoved), args);

    private void NotifyMeasurement(EventHandler<MeasurementEventArgs>? handlers, MeasurementEventArgs args)
    {
        foreach (EventHandler<MeasurementEventArgs> handler in handlers?.GetInvocationList() ?? [])
            _runtime.Measurements.Notifications.Post(() => handler(this, args));
    }

}
