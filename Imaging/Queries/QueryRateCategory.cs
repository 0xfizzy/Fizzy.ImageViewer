namespace Fizzy.ImageViewer.Imaging.Queries;

/// <summary>Selects a configured rate independently of the query's execution protocol.</summary>
internal enum QueryRateCategory
{
    Pixel,
    Line,
    Region
}
