namespace Fizzy.ImageViewer.Measurements;

/// <summary>Reusable tool registration. Each activation creates an independent session on the viewer STA.</summary>
public interface IMeasurementTool
{
    /// <summary>Stable, nonblank, case-sensitive registration ID, captured once at registration.</summary>
    string Id { get; }
    /// <summary>Nonblank menu label, captured once at registration.</summary>
    string DisplayName { get; }
    /// <summary>Returns a new session for this activation. Keep mutable interaction state in that session.</summary>
    IMeasurementToolSession CreateSession(IMeasurementToolContext context);
}
