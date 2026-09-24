using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Fizzy.ImageViewer.Measurements;

internal static class MeasurementFailure
{
    [DoesNotReturn]
    internal static void RethrowAfterCleanup(Exception original, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception failure)
        { throw new AggregateException("Measurement operation and cleanup failed.", original, failure); }
        ExceptionDispatchInfo.Capture(original).Throw();
    }
}
