using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Viewport;

namespace Fizzy.ImageViewer.Tests;

internal static class FrameDisplayTestExtensions
{
    // Callers explicitly show/load the host before requesting a committed frame.
    internal static async ValueTask<FrameSubmitResult> SubmitAndDisplayAsync(this IViewerCapabilities viewer,
        ImageFrame frame, FrameSubmissionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(object? sender, DisplayFailure failure) => committed.TrySetException(failure.Error);
        viewer.DisplayFailed += Failed;
        try
        {
            var result = await viewer.SubmitFrameAsync(frame, (options ?? new()) with
            {
                OnCommitted = lease =>
                {
                    try { options?.OnCommitted?.Invoke(lease); }
                    finally
                    {
                        var control = viewer is Viewer host ? host.Host.Control : (ImageViewerControl)viewer;
                        // Observe completion after public subscribers have run as well.
                        control.Dispatcher.BeginInvoke(() => committed.TrySetResult());
                    }
                }
            }, cancellationToken);
            if (result.Status == FrameSubmitStatus.Accepted)
                await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return result;
        }
        finally { viewer.DisplayFailed -= Failed; }
    }
}
