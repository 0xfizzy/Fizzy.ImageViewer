using System.Collections.Generic;
using System.Windows;

namespace Fizzy.ImageViewer.Editing;

/// <summary>
/// Stores edit-related data for a shape.
/// </summary>
public sealed class EditData
{
    /// <summary>
    /// Control point handles rendered when shape is in edit mode.
    /// </summary>
    public List<UIElement> ControlPointHandles { get; set; } = [];

    /// <summary>
    /// Shape-specific editor instance.
    /// </summary>
    public IShapeEditor? Editor { get; set; }
}
