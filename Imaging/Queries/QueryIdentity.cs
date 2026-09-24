namespace Fizzy.ImageViewer.Imaging.Queries;

internal readonly record struct QueryIdentity(Guid ClientId, long GeometryVersion, long SessionVersion = 0);
