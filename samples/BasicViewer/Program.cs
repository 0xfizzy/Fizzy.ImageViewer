using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
viewer.DisplayFailed += (_, failure) => Console.Error.WriteLine($"Display failed: {failure.Error}");
viewer.Title = "Basic viewer — close the window to exit";
var pixels = Enumerable.Range(0, 640 * 480).Select(i => (byte)(i % 640)).ToArray();
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
Console.WriteLine(result);
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Accepted)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await closed.Task;
// await using observes shutdown failures and waits for the viewer STA to exit.
