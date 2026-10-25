using System.Windows;
using System.IO;
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Snapshots;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
viewer.MeasurementCompleted += (_, e) => Console.WriteLine($"Completed: {e.Snapshot}");
viewer.MeasurementChanged += (_, e) => Console.WriteLine($"Changed: {e.Snapshot}; query: {e.QueryResult}");
using var registration = viewer.RegisterMeasurementTool(new PointTool());
var pixels = Enumerable.Range(0, 640 * 480).Select(i => (byte)i).ToArray();
var displayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
viewer.FrameCommitted += (_, _) => displayed.TrySetResult();
viewer.DisplayFailed += (_, failure) =>
{
    Console.Error.WriteLine($"Display failed: {failure.Error}");
    displayed.TrySetException(failure.Error);
};
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Accepted)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await displayed.Task.WaitAsync(TimeSpan.FromSeconds(10));
using (var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display))
{
    var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/samples");
    Directory.CreateDirectory(directory);
    await snapshot.SaveAsync(Path.Combine(directory, "snapshot.png"), SnapshotEncoding.Png);
}
viewer.Title = "Click to create a point; right-click for built-in measurement tools";
viewer.ActivateMeasurementTool("sample.point");
viewer.Show();
await closed.Task;

sealed class PointTool : IMeasurementTool
{
    public string Id => "sample.point";
    public string DisplayName => "Sample point";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context) => new Session(context);
    private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
    {
        public MeasurementClickResult OnClick(Point point)
        {
            var measurement = context.CreateMeasurement(MeasurementGeometry.Point(point));
            measurement.Complete();
            return MeasurementClickResult.Finish;
        }
        public void OnMouseMove(Point point) { }
        public void Cancel() { }
        public void Dispose() { }
    }
}
