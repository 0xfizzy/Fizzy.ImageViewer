using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class MeasurementDegenerateGeometryTests
{
    [Theory]
    [InlineData("circle")]
    [InlineData("line")]
    [InlineData("rectangle-point")]
    [InlineData("rectangle-width")]
    [InlineData("rectangle-height")]
    public async Task OverlappingControlsCanExpandEveryDegenerateGeometry(string shape)
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            MeasurementGeometry initial = shape switch
            {
                "circle" => MeasurementGeometry.Circle(new(), 0),
                "line" => MeasurementGeometry.Line(new(), new()),
                "rectangle-width" => MeasurementGeometry.Rectangle(new(), new(0, 4)),
                "rectangle-height" => MeasurementGeometry.Rectangle(new(), new(4, 0)),
                _ => MeasurementGeometry.Rectangle(new(), new())
            };
            using var handle = new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame).CreateMeasurement(initial);
            handle.Complete();
            var item = (MeasurementItem)handle;
            var editor = viewer.Host.Interaction.Editor;
            Assert.True(editor.StartEditing(item));
            Assert.True(editor.BeginDrag(new(), 1));
            editor.UpdateDrag(new(5, 7));
            editor.EndDrag();
            switch (handle.Geometry)
            {
                case CircleMeasurementGeometry circle:
                    Assert.Equal(new Point(), circle.Center);
                    Assert.Equal(Math.Sqrt(74), circle.Radius);
                    break;
                case LineMeasurementGeometry line:
                    Assert.Equal(new Point(), line.Start);
                    Assert.Equal(new Point(5, 7), line.End);
                    break;
                case RectangleMeasurementGeometry rectangle:
                    Assert.True(rectangle.Bounds.Width > 0);
                    Assert.True(rectangle.Bounds.Height > 0);
                    Assert.Contains(new Point(5, 7), rectangle.ControlPoints);
                    break;
            }
            Assert.Equal(handle.Geometry.ControlPoints.Count, editor.Handles.Count);
            for (int index = 0; index < editor.Handles.Count; index++)
            {
                Assert.Equal(handle.Geometry.ControlPoints[index].X, System.Windows.Controls.Canvas.GetLeft(editor.Handles[index]));
                Assert.Equal(handle.Geometry.ControlPoints[index].Y, System.Windows.Controls.Canvas.GetTop(editor.Handles[index]));
            }
        });
    }

    [Fact]
    public async Task CircleCanCollapseThenExpandAgainInSameEditingSession()
    {
        await using var viewer = new Viewer(NullLogger<Viewer>.Instance, new WriteableBitmapPresenter(), false);
        await viewer.Host.Window.Dispatcher.InvokeAsync(() =>
        {
            using var handle = new MeasurementCreationContext(viewer.Host.Measurements, viewer.Host.MeasurementRuntime, viewer.AcquireCurrentFrame)
                .CreateMeasurement(MeasurementGeometry.Circle(new(), 4));
            handle.Complete();
            var editor = viewer.Host.Interaction.Editor;
            Assert.True(editor.StartEditing((MeasurementItem)handle));
            Assert.True(editor.BeginDrag(new(4, 0), 1));
            editor.UpdateDrag(new()); editor.EndDrag();
            Assert.Equal(0, Assert.IsType<CircleMeasurementGeometry>(handle.Geometry).Radius);
            Assert.True(editor.BeginDrag(new(), 1));
            editor.UpdateDrag(new(5, 0)); editor.EndDrag();
            var circle = Assert.IsType<CircleMeasurementGeometry>(handle.Geometry);
            Assert.Equal(new Point(), circle.Center);
            Assert.Equal(5, circle.Radius);
        });
    }
}
