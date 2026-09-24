namespace Fizzy.ImageViewer.Measurements;

/// <summary>STA-owned tool registrations and immutable registration metadata.</summary>
internal sealed class MeasurementToolRegistry
{
    internal sealed record Registration(string Id, string DisplayName, IMeasurementTool Tool);
    private readonly Dictionary<string, Registration> _tools = new(StringComparer.Ordinal);
    internal Registration[] RegisteredTools => _tools.Values.ToArray();
    internal void RegisterTool(IMeasurementTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var id = tool.Id; var name = tool.DisplayName;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_tools.TryAdd(id, new(id, name, tool))) throw new ArgumentException($"Measurement tool '{id}' is already registered.", nameof(tool));
    }
    internal bool UnregisterTool(string id) => _tools.Remove(id);
    internal bool HasTool(string id) => _tools.ContainsKey(id);
    internal IMeasurementTool? Find(string id) => _tools.GetValueOrDefault(id)?.Tool;
    internal void Clear() => _tools.Clear();
}
