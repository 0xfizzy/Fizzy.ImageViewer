using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

[Collection("Viewer")]
public class QuerySchedulingTests
{
    private static Viewer Viewer()=>new(NullLogger<Viewer>.Instance,new Rendering.WriteableBitmapPresenter(),false);
    private static ImageFrame Frame(ControlledSource source,Action release)=>new(new FrameStorage(new(2,1,2,FramePixelFormat.Gray8),new byte[]{7,8},release,0,source));
    [Fact]
    public async Task ClosedLineStrengthDiscardsInFlightSamples()
    {
        await using var viewer=Viewer();
        var source=new ControlledSource();
        System.Windows.Window? window=null;
        ScottPlot.WPF.WpfPlot? plot=null;
        await viewer.UiDispatcher.InvokeAsync(()=>
        {
            var method=new MeasureMethods.LineStrengthMeasure();
            method.OnClick(new(0,0),viewer.MeasurementContext);
            method.OnClick(new(1,0),viewer.MeasurementContext);
            window=System.Windows.PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>()
                .Select(s=>s.RootVisual).OfType<System.Windows.Window>().Single(w=>w.Content is ScottPlot.WPF.WpfPlot);
            plot=(ScottPlot.WPF.WpfPlot)window.Content;
        });
        await viewer.SubmitFrameAsync(Frame(source,()=>{}));
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await viewer.UiDispatcher.InvokeAsync(()=>window!.Close());
        }
        finally { source.Release.TrySetResult(); }
        await viewer.MeasurementContext.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await viewer.UiDispatcher.InvokeAsync(()=>
        {
            Assert.False(window!.IsVisible);
            Assert.Empty(plot!.Plot.GetPlottables());
            var overlay=viewer.Layers.Measurements.Root.Children.OfType<Controls.OverlayLayer>().Single();
            Assert.Empty(overlay.Canvas.Children.Cast<System.Windows.UIElement>());
        });
    }
    [Fact]
    public async Task SlowBatchDoesNotBlockUiAndGeometryChangesInvalidateResults()
    {
        await using var viewer=Viewer();viewer.QueryOptions=new(){MaxResultAge=TimeSpan.FromSeconds(5)};
        var source=new ControlledSource();int released=0;
        var a=new Client();var b=new Client();
        await viewer.SubmitFrameAsync(Frame(source,()=>released++));
        await viewer.UiDispatcher.InvokeAsync(()=>{viewer.MeasurementContext.Register(a);viewer.MeasurementContext.Register(b);});
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2,source.PointCount);Assert.NotEqual(ApartmentState.STA,source.Apartment);
        await viewer.UiDispatcher.InvokeAsync(()=>a.Revision++).Task.WaitAsync(TimeSpan.FromSeconds(1));
        var commit=await viewer.SubmitFrameAsync(ImageFrame.Copy(new(2,1,2,FramePixelFormat.Gray8),new byte[]{9,10}));
        Assert.Equal(FrameSubmitStatus.Committed,commit.Status);Assert.Equal(0,released);
        source.Release.SetResult();
        await a.Published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(42d,a.Values);Assert.Equal(9,a.Values.Last());
        Assert.Equal(1,released);
    }
    [Fact]
    public async Task ExpiredResultIsDroppedAndCloseWaitsForActiveQuery()
    {
        var viewer=Viewer();viewer.QueryOptions=new(){MaxResultAge=TimeSpan.FromMilliseconds(5)};
        var source=new ControlledSource();int released=0;var item=new Client();
        await viewer.SubmitFrameAsync(Frame(source,()=>released++));
        await viewer.UiDispatcher.InvokeAsync(()=>viewer.MeasurementContext.Register(item));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));await Task.Delay(25);
        // Raising the rate threshold does not retroactively accept this result.
        source.Release.SetResult();
        var deadline=DateTime.UtcNow.AddSeconds(3);
        while(viewer.QueryMetrics.ExpiredResults==0&&DateTime.UtcNow<deadline)await Task.Delay(10);
        Assert.True(viewer.QueryMetrics.ExpiredResults>0);
        await viewer.DisposeAsync();Assert.Equal(1,released);

        viewer=Viewer();source=new ControlledSource();released=0;item=new Client();
        await viewer.SubmitFrameAsync(Frame(source,()=>released++));
        await viewer.UiDispatcher.InvokeAsync(()=>viewer.MeasurementContext.Register(item));
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var close=viewer.CloseAsync().AsTask();await Task.Delay(20);Assert.False(close.IsCompleted);Assert.Equal(0,released);
        source.Release.SetResult();await close.WaitAsync(TimeSpan.FromSeconds(3));Assert.Equal(1,released);Assert.Empty(item.Values);
    }
    [Fact]
    public async Task UnsupportedStatisticsDoNotDiscardSuccessfulGather()
    {
        await using var viewer=Viewer();
        viewer.QueryOptions=new(){MaxResultAge=TimeSpan.FromSeconds(5)};
        var source=new ControlledSource();source.Release.SetResult();
        var pixel=new Client();var region=new UnsupportedRegion();
        await viewer.SubmitFrameAsync(Frame(source,()=>{}));
        await viewer.UiDispatcher.InvokeAsync(()=> {
            viewer.MeasurementContext.Register(pixel);
            viewer.MeasurementContext.Register(region);
        });
        await pixel.Published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(42,pixel.Values[0]);
        Assert.False(region.Published);
    }
    private sealed class UnsupportedRegion : IFrameMeasurement
    {
        public bool Published;
        public QueryRequest? Capture(FrameDescriptor descriptor)=>new(0,QueryKind.Region,null,new PixelRegion(0,0,1,1),(_,_)=>Published=true);
        public void ClearResult() { }
        public void Dispose() { }
    }
    private sealed class ControlledSource : IFramePixelSource
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PointCount;public ApartmentState Apartment;
        public async ValueTask<PixelSample[]> ReadPixelsAsync(ReadOnlyMemory<PixelCoordinate> coordinates,CancellationToken ct)
        {
            PointCount=coordinates.Length;Apartment=Thread.CurrentThread.GetApartmentState();Entered.TrySetResult();await Release.Task;
            return Enumerable.Repeat(new PixelSample(FramePixelFormat.Gray8,42,0,0,0,255),coordinates.Length).ToArray();
        }
        public ValueTask<RegionStatistics> ComputeRegionStatisticsAsync(PixelRegion region,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<ImageFrame> ReadRegionAsync(PixelRegion region,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Client : IFrameMeasurement
    {
        public int Revision;public List<double> Values=[];public TaskCompletionSource Published=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryRequest? Capture(FrameDescriptor descriptor)=>new(Revision,QueryKind.Pixel,[new(0,0)],null,(samples,_)=>{Values.Add(samples![0].Gray);Published.TrySetResult();});
        public void ClearResult() { }
        public void Dispose() { }
    }
}
