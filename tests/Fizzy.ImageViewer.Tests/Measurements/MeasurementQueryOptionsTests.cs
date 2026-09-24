using Fizzy.ImageViewer.Measurements;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class MeasurementQueryOptionsTests
{
    [Fact]
    public void QueryConfigurationOwnsOnlyApplicablePresentationOptions()
    {
        Assert.Equal(MeasurementQuery.None, new MeasurementOptions().Query.Kind);
        Assert.False(MeasurementQueryOptions.LineProfile.ShowWindow);
        var profile = new LineProfileMeasurementQueryOptions(showWindow: true);
        Assert.Equal(MeasurementQuery.LineProfile, profile.Kind);
        Assert.True(profile.ShowWindow);
        new MeasurementOptions { Query = profile }.Validate(MeasurementGeometry.Line(new(), new(1, 1)));
        Assert.Throws<ArgumentException>(() => new MeasurementOptions { Query = profile }
            .Validate(MeasurementGeometry.Point(new())));
        Assert.Throws<ArgumentNullException>(() => new MeasurementOptions { Query = null! }
            .Validate(MeasurementGeometry.Point(new())));
    }

    [Fact]
    public void EveryQueryAcceptsItsGeometryAndRejectsIncompatibleShapes()
    {
        var point = MeasurementGeometry.Point(new());
        var crosshair = MeasurementGeometry.Crosshair(new());
        var line = MeasurementGeometry.Line(new(), new(1, 1));
        var rectangle = MeasurementGeometry.Rectangle(new(), new(1, 1));
        var circle = MeasurementGeometry.Circle(new(), 1);
        var geometries = new MeasurementGeometry[] { point, crosshair, line, rectangle, circle };
        var queries = new[] { MeasurementQueryOptions.None, MeasurementQueryOptions.Pixel,
            MeasurementQueryOptions.LineProfile, MeasurementQueryOptions.RegionStatistics };
        foreach (var query in queries)
        foreach (var geometry in geometries)
        {
            var options = new MeasurementOptions { Query = query };
            bool supported = query.Kind switch
            {
                MeasurementQuery.None => true,
                MeasurementQuery.Pixel => geometry == point || geometry == crosshair,
                MeasurementQuery.LineProfile => geometry == line,
                _ => geometry == rectangle
            };
            if (supported) options.Validate(geometry);
            else Assert.Throws<ArgumentException>(() => options.Validate(geometry));
        }
    }

    [Fact]
    public void QueryConfigurationCannotBeExtendedOrMutatedByConsumers()
    {
        var constructors = typeof(MeasurementQueryOptions).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.True(constructor.IsFamilyAndAssembly));
        Assert.True(typeof(LineProfileMeasurementQueryOptions).IsSealed);
        Assert.Null(typeof(LineProfileMeasurementQueryOptions).GetProperty(nameof(LineProfileMeasurementQueryOptions.ShowWindow))!.SetMethod);
        Assert.Null(typeof(MeasurementQueryOptions).GetProperty(nameof(MeasurementQueryOptions.Kind))!.SetMethod);
    }
}
