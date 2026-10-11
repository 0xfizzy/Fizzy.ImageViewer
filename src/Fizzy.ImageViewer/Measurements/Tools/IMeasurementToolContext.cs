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
    /// <remarks>Session and preview cleanup failures propagate after all cleanup is attempted.
    /// The activation remains ended and subsequent calls do not retry cleanup.</remarks>
    bool Finish();
    /// <summary>Viewer measurement style available to this activation.</summary>
    MeasurementStyle Style { get; }
    /// <summary>Acquires an owned lease to the current committed frame, or null if none exists. Dispose the lease; it may outlive this activation.</summary>
    FrameLease? AcquireCurrentFrame();
    /// <summary>Creates a model-owned preview. Complete retains it after the tool finishes.</summary>
    IMeasurement CreateMeasurement(MeasurementGeometry geometry, MeasurementOptions? options = null);
}
