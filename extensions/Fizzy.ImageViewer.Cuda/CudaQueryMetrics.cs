namespace Fizzy.ImageViewer.Cuda;

/// <summary>Extension query counters; scratch allocation/reuse and quarantined bytes belong to the bridge.</summary>
/// <param name="Queries">Queries successfully enqueued by native code.</param>
/// <param name="DeviceToHostBytes">Requested result bytes enqueued for transfer.</param>
/// <param name="ResultBuffers">Cumulative host result buffers used by queries.</param>
/// <param name="LiveRequests">Currently executing native query callbacks.</param>
/// <param name="ResultBytes">Host result bytes of currently executing callbacks, not quarantined storage.</param>
public readonly record struct CudaQueryMetrics(long Queries, long DeviceToHostBytes, long ResultBuffers, long LiveRequests, long ResultBytes);
