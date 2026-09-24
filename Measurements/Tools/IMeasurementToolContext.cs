using Fizzy.ImageViewer.Frames;

namespace Fizzy.ImageViewer.Measurements;

/// <summary>Capabilities borrowed by custom tools on the viewer STA. Tool callbacks and
/// creation operations must run on that STA; returned measurement handles dispatch themselves.
/// Each factory receives a context for one activation and passes it to its session. Ended contexts reject new measurements.</summary>
public interface IMeasurementToolContext
{
    /// <summary>Immutable registration and activation identity shared by this session's measurements.</summary>
    MeasurementOrigin Origin { get; }
    /// <summary>Ends only this activation, retaining completed measurements and disposing previews.
    /// May be called from any thread. Returns false if this activation has already ended.</summary>
    bool Finish();
    MeasurementStyle Style { get; }
    FrameLease? AcquireCurrentFrame();
    /// <summary>Creates a model-owned preview. Complete retains it after the tool finishes.</summary>
    IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null);
}
