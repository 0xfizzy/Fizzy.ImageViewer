namespace Fizzy.ImageViewer.Imaging;

/// <summary>A snapshot of shared query execution and publication-age counters.</summary>
/// <param name="Batches">Batches whose backend execution completed, including batches containing
/// failed queries. Cancellation or failure of the batch execution itself does not increment this count.
/// This is not a count of successful queries or published results.</param>
/// <param name="ExpiredResults">Individual completed results rejected at publication because their
/// capture age exceeded MaxResultAge. Expiration of an already displayed result is not counted.</param>
/// <param name="LastDurationMilliseconds">Elapsed time from frame/geometry capture to completion of
/// the latest counted batch's backend execution, including worker scheduling. Excludes waiting for
/// STA publication and publication callbacks; this is not end-to-end display latency.</param>
public readonly record struct PixelQueryMetrics(long Batches, long ExpiredResults, double LastDurationMilliseconds);
