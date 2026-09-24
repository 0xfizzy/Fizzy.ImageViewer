namespace Fizzy.ImageViewer.Imaging.Queries;

internal enum QueryIntervalOrigin
{
    Start,
    // The STA processing the completion, including rejected or failed results.
    Completion
}
