namespace ApplicationEndpoint;

internal interface IImageNotifications
{
    // Subscribe, unsubscribe and handle notifications on the host UI thread.
    event EventHandler<LengthResult>? LengthCompleted;
    event EventHandler<string>? DisplayFailed;
}
