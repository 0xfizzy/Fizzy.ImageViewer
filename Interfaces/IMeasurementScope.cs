using System;
using System.Windows;

namespace Fizzy.ImageViewer.Interfaces;

/// <summary>Owns custom measurement resources. All operations run on the viewer STA.
/// Dispose cancels/removes the scope; Complete retains it until deletion, clear or viewer closure.</summary>
public interface IMeasurementScope : IDisposable
{
    void AddShape(UIElement shape);
    void AddResource(IDisposable resource);
    void OnDispose(Action callback);
    void Complete();
}
