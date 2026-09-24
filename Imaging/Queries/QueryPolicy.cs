namespace Fizzy.ImageViewer.Imaging.Queries;

internal readonly record struct QueryPolicy(bool AllowMovingResult = false, double? MaximumRate = null, TimeSpan? DisplayAge = null);
