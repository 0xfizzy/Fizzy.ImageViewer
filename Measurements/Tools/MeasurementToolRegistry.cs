namespace Fizzy.ImageViewer.Measurements;

/// <summary>STA-owned tool registrations and immutable registration metadata.</summary>
internal sealed class MeasurementToolRegistry
{
    internal sealed record Registration(string Id, string DisplayName, IMeasurementTool Tool)
    {
        internal MeasurementToolRegistration? Handle { get; set; }
        internal void Invalidate() { Handle?.Invalidate(); Handle = null; }
    }
    private readonly Dictionary<string, Registration> _tools = new(StringComparer.Ordinal);
    internal Registration[] RegisteredTools => _tools.Values.ToArray();
    internal Registration RegisterTool(IMeasurementTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var id = tool.Id; var name = tool.DisplayName;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var registration = new Registration(id, name, tool);
        if (!_tools.TryAdd(id, registration)) throw new ArgumentException($"Measurement tool '{id}' is already registered.", nameof(tool));
        return registration;
    }
    internal bool UnregisterTool(Registration registration)
    {
        if (!Contains(registration)) return false;
        _tools.Remove(registration.Id);
        registration.Invalidate();
        return true;
    }
    internal IMeasurementTool? Find(string id) => _tools.GetValueOrDefault(id)?.Tool;
    internal Registration? FindRegistration(string id) => _tools.GetValueOrDefault(id);
    internal bool Contains(Registration registration) => ReferenceEquals(FindRegistration(registration.Id), registration);
    internal void Clear()
    {
        foreach (var registration in _tools.Values) registration.Invalidate();
        _tools.Clear();
    }
}
