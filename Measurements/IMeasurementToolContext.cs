using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Capabilities borrowed by custom tools on the viewer STA. Tool callbacks and
/// measurement operations must run on that STA; retain resources through a measurement.
/// Each factory receives a context for one activation and passes it to its session. Ended contexts reject new measurements.</summary>
public interface IMeasurementToolContext
{
    MeasurementStyle Style { get; }
    FrameLease? AcquireCurrentFrame();
    /// <summary>Creates a model-owned preview. Complete retains it after the tool finishes.</summary>
    IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null);
}
