namespace ApplicationEndpoint;

internal sealed class SyntheticProducer(IImageSink sink)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        const int width = 640, height = 480;
        int phase = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = new ArrayMemoryOwner(width * height);
            GrayFrame frame;
            try
            {
                Fill(owner.Memory, width, height, phase++);
                frame = new GrayFrame(width, height, width, owner);
            }
            catch { owner.Dispose(); throw; }
            // Ownership transfers at invocation, even when the endpoint rejects the frame.
            await sink.PublishAsync(frame, cancellationToken).ConfigureAwait(false);
            await Task.Delay(33, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Fill(Memory<byte> memory, int width, int height, int phase)
    {
        var pixels = memory.Span;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) pixels[y * width + x] = (byte)(x + y + phase);
    }
}
