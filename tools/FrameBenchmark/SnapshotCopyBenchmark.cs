using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Fizzy.ImageViewer.Frames;

internal static class SnapshotCopyBenchmark
{
    internal static async Task RunAsync(string output)
    {
        var results = new List<object>();
        foreach (var (width, height) in new[] { (1920, 1080), (3840, 2160) })
        foreach (var format in new[] { FramePixelFormat.Gray8, FramePixelFormat.Bgr24 })
        {
            var descriptor = new FrameDescriptor(width, height, width * format.BytesPerPixel(), format);
            using var frame = ImageFrame.Copy(descriptor, new byte[descriptor.RequiredBytes]);
            using var source = frame.Acquire();
            foreach (bool copyAgain in new[] { true, false })
            {
                for (int warmup = 0; warmup < 5; warmup++) await Capture();
                long allocated = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                for (int iteration = 0; iteration < 30; iteration++) await Capture();
                results.Add(new { width, height, format = format.ToString(), copyAgain,
                    meanMilliseconds = timer.Elapsed.TotalMilliseconds / 30,
                    allocatedBytesPerCapture = (GC.GetTotalAllocatedBytes(true) - allocated) / 30,
                    simultaneousPixelStorageBytes = (long)descriptor.RequiredBytes * (copyAgain ? 2 : 1) });

                async Task Capture()
                {
                    using var region = await source.ReadRegionAsync(PixelRegion.Full(descriptor));
                    if (copyAgain)
                    {
                        using var pixels = region.AcquirePixels();
                        using var copied = ImageFrame.Copy(pixels.Descriptor, pixels.CpuPixels.Span);
                    }
                    else
                    {
                        using var transferred = region.TransferPixels();
                    }
                }
            }
        }
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            note = "Isolates Raw region read plus extra Copy versus ownership transfer. Not an end-to-end historical binary comparison. Pixel storage is logical concurrently live region/output bytes, excluding source and pool capacity; it is not process peak memory.",
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
