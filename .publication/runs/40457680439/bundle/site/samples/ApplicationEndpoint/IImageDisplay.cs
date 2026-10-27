namespace ApplicationEndpoint;

internal interface IImageDisplay
{
    // Called by the ViewModel on the host UI thread.
    void FitImage();
    void StartLengthMeasurement();
}
