namespace Fizzy.ImageViewer.Imaging.Queries;

internal readonly record struct QueryPolicy(
    bool AllowPreviousGeometry = false,
    QueryIntervalOrigin IntervalOrigin = QueryIntervalOrigin.Start,
    double? MaximumRate = null,
    TimeSpan? DisplayRetentionAge = null);
