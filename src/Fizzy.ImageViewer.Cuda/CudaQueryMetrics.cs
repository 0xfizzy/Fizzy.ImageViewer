namespace Fizzy.ImageViewer.Cuda;

/// <summary>Extension query counters; scratch allocation/reuse and quarantined bytes belong to the bridge.</summary>
/// <param name="EnqueuedQueries">Queries successfully enqueued by native code.</param>
/// <param name="EnqueuedResultBytes">Requested result bytes enqueued for transfer.</param>
/// <param name="ActiveQueries">Currently executing native query callbacks.</param>
/// <param name="ActiveResultBytes">Host result bytes of currently executing callbacks, not quarantined storage.</param>
public readonly record struct CudaQueryMetrics(long EnqueuedQueries, long EnqueuedResultBytes, long ActiveQueries, long ActiveResultBytes);
