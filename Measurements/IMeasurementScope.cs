using Fizzy.ImageViewer.Drawing;
using System;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Owns custom measurement resources. All operations run on the viewer STA.
/// Dispose cancels/removes the scope; Complete retains it until deletion, clear or viewer closure.</summary>
public interface IMeasurementScope : IDisposable
{
    void AddShape(UIElement shape);
    /// <summary>Moves an owned Shapes.Create* visual in image coordinates, preserving its zoom policy.</summary>
    void UpdateAnchor(UIElement shape, Point anchor);
    void AddResource(IDisposable resource);
    void OnDispose(Action callback);
    void Complete();
}
