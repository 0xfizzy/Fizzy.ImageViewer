using System.Windows;
using System.Windows.Controls;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer;

/// <summary>UI-thread owner of one measurement, including its query and optional result window.</summary>
internal class MeasurementItem : IFrameMeasurement, IDisposable
{
    protected readonly MeasureContext Context;
    private readonly MeasurementDisplayAdapter _display;
    private MeasurementSubscription? _subscription;
    private Window? _window;
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public UIElement PrimaryVisual { get; }
    public TextBlock Label { get; }
    public bool IsDisposed { get; private set; }
    public bool IsComplete { get; private set; }
    public object? Result { get; protected set; }
    public long? ResultFrameId { get; private set; }
    protected Window? ResultWindow => _window;
    public IEnumerable<UIElement> Visuals => [PrimaryVisual, Label];

    public MeasurementItem(MeasureContext context, MeasurementGeometry geometry, UIElement primary, TextBlock label)
    {
        Context = context; Geometry = geometry; PrimaryVisual = primary; Label = label;
        _display = new(context, primary, label);
        _display.Apply(geometry);
        UpdateText();
        context.Attach(this);
    }

    public void UpdateGeometry(MeasurementGeometry geometry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (geometry.Kind != Geometry.Kind) throw new ArgumentException("Geometry kind cannot change.", nameof(geometry));
        if (geometry.Start == Geometry.Start && geometry.End == Geometry.End) return;
        Geometry = geometry.WithVersion(Geometry.Version + 1);
        _display.Apply(Geometry);
        ClearResult();
    }

    public virtual void Complete()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsComplete = true;
        UpdateText();
    }
    protected void Subscribe() => _subscription ??= Context.Register(this);
    protected void OwnWindow(Window window)
    {
        _window = window;
        window.Closed += WindowClosed;
    }
    private void WindowClosed(object? sender, EventArgs args)
    {
        if (_window != null) _window.Closed -= WindowClosed;
        _window = null;
        Dispose();
    }
    public virtual QueryRequest? Capture(FrameDescriptor descriptor) => null;
    public void ResultPublished(long frameId) => ResultFrameId = frameId;
    public virtual void ClearResult()
    {
        Result = null; ResultFrameId = null;
        if (!IsDisposed) UpdateText();
    }
    protected virtual void UpdateText() => Label.Text = Geometry.Kind switch
    {
        ShapeType.Line => $"{(Geometry.End - Geometry.Start).Length:F1} px",
        ShapeType.Point => $"X:{Geometry.X:F2}\nY:{Geometry.Y:F2}",
        _ => "等待数据"
    };
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _subscription?.Dispose(); _subscription = null;
        var window = _window; _window = null;
        if (window != null) window.Closed -= WindowClosed;
        // Every step is attempted even when an external WPF event handler throws.
        try { window?.Close(); }
        finally
        {
            try { Context.Detach(this); }
            finally { Result = null; ResultFrameId = null; }
        }
    }
}
