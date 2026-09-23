using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Menus;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    // Capture interaction once per opening. Menu rendering knows no measurement policy.
    private IEnumerable<IMenuItem> CreateInteractionMenu()
    {
        var interaction = _interaction!;
        var measuring = _measureManager!.IsMeasuring;
        var selected = interaction.SelectedShape;
        if (measuring)
        {
            yield return new MenuItem("Cancel Measurement", interaction.Cancel);
            yield break;
        }
        if (selected != null)
        {
            yield return new MenuItem("Edit", () => interaction.StartEditing(selected));
            yield return new MenuItem("Delete", () => interaction.Delete(selected));
            yield return SeparatorMenuItem.Instance;
        }
        foreach (var tool in _measureManager.RegisteredTools)
            yield return new MenuItem(tool.DisplayName, () =>
            {
                if (_measureManager.HasTool(tool.Id) && Layers.Measurements.IsVisible)
                    interaction.StartMeasurement(tool.Id);
            });
    }
}
