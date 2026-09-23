using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Controls;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>UI-thread owner of measurement geometry and visuals. Specialized measurements own their resources.</summary>
internal class MeasurementItem : IDisposable
{
    protected readonly IMeasurementContext Context;
    private readonly MeasurementDisplayAdapter _display;
    public Guid Id { get; } = Guid.NewGuid();
    public MeasurementGeometry Geometry { get; private set; }
    public UIElement PrimaryVisual { get; }
    public TextBlock Label { get; }
    public bool IsDisposed { get; private set; }
    public bool IsComplete { get; private set; }
    public IEnumerable<UIElement> Visuals => [PrimaryVisual, Label];

    public MeasurementItem(IMeasurementContext context, MeasurementGeometry geometry, UIElement primary, TextBlock label)
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
        OnGeometryChanged();
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (IsComplete) return;
        IsComplete = true;
        UpdateText();
        OnComplete();
        if (!IsDisposed) Context.NotifyCompleted(this);
    }
    protected virtual void OnComplete() { }
    internal bool CompletionNotified { get; set; }
    protected virtual void OnGeometryChanged() => UpdateText();
    protected virtual void OnDisposing() { }
    protected void UpdateText() => Label.Text = Geometry.Kind switch
    {
        ShapeType.Line => $"{(Geometry.End - Geometry.Start).Length:F1} px",
        ShapeType.Point => $"X:{Geometry.X:F2}\nY:{Geometry.Y:F2}",
        _ => "Waiting for data"
    };
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        // Visual detachment must still run when a specialized resource fails to close.
        try { OnDisposing(); }
        finally { Context.Detach(this); }
    }
}
