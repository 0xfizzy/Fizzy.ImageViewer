using Fizzy.ImageViewer.Frames;
using System.Windows;

namespace Fizzy.ImageViewer;

public partial class ImageViewerControl
{
    /// <summary>Identifies the one-way image-source binding property.</summary>
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageBindingSource), typeof(ImageViewerControl),
        new PropertyMetadata(null, SourceChanged, CoerceSource));

    private static readonly DependencyPropertyKey ImageSourceSubmitResultPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ImageSourceSubmitResult), typeof(FrameSubmitResult?), typeof(ImageViewerControl), new PropertyMetadata(null));

    /// <summary>Identifies the most recent binding reception result.</summary>
    public static readonly DependencyProperty ImageSourceSubmitResultProperty = ImageSourceSubmitResultPropertyKey.DependencyProperty;

    /// <summary>Gets or sets a source owned by this control's host dispatcher.</summary>
    /// <remarks>Property assignment borrows the source; it never consumes a caller's frame. Null or clearing
    /// the binding detaches the source and preserves the last displayed image. Unloading retains the
    /// association. Final disposal detaches it without disposing the source. A disposed source is empty.
    /// Emptying or detaching does not retract already accepted input, which may still display.
    /// Explicit submissions and source updates use the same latest-frame pipeline in reception order;
    /// an unchanged source does not overwrite subsequent explicit submissions.</remarks>
    public ImageBindingSource? ImageSource
    {
        get => (ImageBindingSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>Gets the latest source reception result, or null when detached or the source is empty.</summary>
    /// <remarks>Accepted does not mean displayed. Observe DisplayFailed for later rendering errors and
    /// await DisposeAsync for pipeline cleanup errors. Explicit submissions do not change this property.
    /// Frozen updates are not retried automatically. This property has WPF thread affinity.</remarks>
    public FrameSubmitResult? ImageSourceSubmitResult => (FrameSubmitResult?)GetValue(ImageSourceSubmitResultProperty);

    private ImageBindingSource? _imageSource;
    private long _sourceVersion = -1;
    private long _sourceUpdate;

    private static object? CoerceSource(DependencyObject owner, object? value)
    {
        var control = (ImageViewerControl)owner;
        if (value is ImageBindingSource source)
        {
            control._runtime.Lifetime.ThrowIfStopping();
            source.VerifyDispatcher(control.Dispatcher);
        }
        return value;
    }

    private static void SourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var control = (ImageViewerControl)owner;
        if (control._imageSource != null) control._imageSource.Changed -= control.PublishImageSource;
        control._imageSource = (ImageBindingSource?)args.NewValue;
        control._sourceVersion = -1;
        if (control._imageSource != null) control._imageSource.Changed += control.PublishImageSource;
        control.PublishImageSource();
    }

    private void PublishImageSource()
    {
        var source = _imageSource;
        if (source != null && _sourceVersion == source.Version) return;
        _sourceVersion = source?.Version ?? -1;
        var update = ++_sourceUpdate;
        using var frame = _runtime.Lifetime.IsStopping ? null : source?.AcquireFrame();
        // Pipeline reception completes synchronously; only preparation/presentation is asynchronous.
        FrameSubmitResult? result = frame == null ? null : _runtime.Pipeline.SubmitAsync(frame).GetAwaiter().GetResult();
        // Releasing a replaced pipeline frame may reenter source replacement or shutdown.
        if (update == _sourceUpdate)
            SetValue(ImageSourceSubmitResultPropertyKey, result);
    }

    internal void DetachImageSource()
    {
        if (_imageSource != null) _imageSource.Changed -= PublishImageSource;
        _imageSource = null;
        ++_sourceUpdate;
        SetValue(ImageSourceProperty, null);
        SetValue(ImageSourceSubmitResultPropertyKey, null);
    }
}
