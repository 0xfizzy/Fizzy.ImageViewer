namespace ApplicationEndpoint;

internal interface IImageSink
{
    // Consumes input immediately, including rejection, cancellation and failure.
    // Completion means reception, not presentation. Never reuse the input.
    Task PublishAsync(GrayFrame input, CancellationToken cancellationToken);
}
