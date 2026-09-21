using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Imaging;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public sealed class MeasureContext : IDisposable
{
    private readonly OverlayLayer _layer;
    private readonly Func<FrameLease?> _acquire;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly List<IFrameMeasurement> _measurements = [];
    private readonly Dictionary<IFrameMeasurement, State> _states = [];
    private readonly CancellationTokenSource _stop = new();
    private sealed class State { public object? Geometry; public long Frame, Started, Due; public bool Valid; }
    private bool _disposed;
    private long _batches, _expired;
    private double _duration;
    private PixelQueryOptions _options=new();
    public PixelQueryOptions QueryOptions { get=>_options; set { value.Validate(); _options=value; } }
    public PixelQueryMetrics QueryMetrics => new(Interlocked.Read(ref _batches),Interlocked.Read(ref _expired),_duration);
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action<FrameInfo>? FrameCommitted;
    internal MeasureContext(OverlayLayer layer, Func<FrameLease?> acquire, ILogger logger)
    {
        _layer=layer; _acquire=acquire; _logger=logger;
        _timer=new DispatcherTimer(DispatcherPriority.Background,layer.Dispatcher) { Interval=TimeSpan.FromMilliseconds(10) };
        _timer.Tick+=(_,_)=>Tick(); _timer.Start();
    }
    public FrameLease? AcquireCurrentFrame()=>_acquire();
    public void AddShape(UIElement shape)=>_layer.AddShape(shape);
    public void RemoveShape(UIElement shape)=>_layer.RemoveShape(shape);
    public void UpdateAnchor(UIElement shape,Point point)=>_layer.UpdateAnchor(shape,point);
    internal void Register(IFrameMeasurement item) { _measurements.Add(item); _states[item]=new(); }
    internal void Unregister(IFrameMeasurement item) { _measurements.Remove(item); _states.Remove(item); }
    internal void NotifyFrameCommitted(FrameInfo info)
    {
        foreach(Action<FrameInfo> handler in FrameCommitted?.GetInvocationList() ?? [])
            try { handler(info); } catch(Exception ex) { _logger.LogWarning(ex,"Measurement subscriber failed"); }
    }
    private void Tick()
    {
        if(_disposed) return;
        using var current=_acquire(); if(current==null) return;
        var now=Stopwatch.GetTimestamp();
        var due=new List<(IFrameMeasurement Item, QueryRequest Request, State State)>();
        foreach(var item in _measurements.ToArray())
        {
            var state=_states[item]; var request=item.Capture(current.Descriptor);
            if(request==null) { state.Valid=false; item.ClearResult(); continue; }
            if(!Equals(state.Geometry,request.Geometry)) { state.Valid=false; state.Geometry=request.Geometry; item.ClearResult(); state.Due=0; }
            if(state.Valid && state.Frame!=current.Info.FrameId && Stopwatch.GetElapsedTime(state.Started)>_options.MaxResultAge) { state.Valid=false; item.ClearResult(); }
            if(now>=state.Due && (!state.Valid || state.Frame!=current.Info.FrameId)) due.Add((item,request,state));
        }
        if(!Completion.IsCompleted || due.Count==0) return;
        due.Sort((a,b)=>a.State.Due.CompareTo(b.State.Due));
        foreach(var entry in due)
        {
            double rate=entry.Request.Kind==QueryKind.Pixel?_options.PixelRate:entry.Request.Kind==QueryKind.Line?_options.LineRate:_options.RegionRate;
            entry.State.Due=now+(long)(Stopwatch.Frequency/rate);
        }
        Completion=RunAsync(current.Acquire(),due,now);
    }
    private async Task RunAsync(FrameLease frame,List<(IFrameMeasurement Item,QueryRequest Request,State State)> entries,long started)
    {
        try
        {
            // Capture geometry on STA; all sampling, including CPU queries, runs off STA.
            var results=await Task.Run(async ()=> {
                var coordinates=entries.Where(e=>e.Request.Coordinates!=null).SelectMany(e=>e.Request.Coordinates!).ToArray();
                PixelSample[]? samples = [];
                try { if (coordinates.Length != 0) samples = (await frame.ReadPixelsAsync(coordinates,_stop.Token)).Samples; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { samples = null; _logger.LogWarning(ex,"Pixel gather failed"); }
                int offset=0; var output=new List<(PixelSample[]? Samples,RegionStatistics? Stats, bool Success)>();
                foreach(var e in entries)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    if(e.Request.Coordinates is { } points) { output.Add((samples?.AsSpan(offset,points.Length).ToArray(),null,samples != null)); offset+=points.Length; }
                    else
                    {
                        try { output.Add((null,(await frame.ComputeRegionStatisticsAsync(e.Request.Region!.Value,_stop.Token)).Statistics,true)); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { _logger.LogWarning(ex,"Region statistics failed"); output.Add((null,null,false)); }
                    }
                }
                return output;
            },_stop.Token).ConfigureAwait(false);
            _duration=Stopwatch.GetElapsedTime(started).TotalMilliseconds; Interlocked.Increment(ref _batches);
            await _layer.Dispatcher.InvokeAsync(()=> {
                if(_disposed) return;
                for(int i=0;i<entries.Count;i++)
                {
                    var e=entries[i]; if(!_states.TryGetValue(e.Item,out var state) || !ReferenceEquals(state,e.State)) continue;
                    var latest=e.Item.Capture(frame.Descriptor);
                    if(latest==null || !Equals(latest.Geometry,e.Request.Geometry)) continue;
                    if(Stopwatch.GetElapsedTime(started)>_options.MaxResultAge) { Interlocked.Increment(ref _expired); e.Item.ClearResult(); continue; }
                    if (!results[i].Success) { state.Valid=false; e.Item.ClearResult(); continue; }
                    state.Valid=true; state.Frame=frame.Info.FrameId; state.Started=started;
                    e.Request.Publish(results[i].Samples,results[i].Stats);
                }
            },DispatcherPriority.Background,_stop.Token).Task.ConfigureAwait(false);
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { _logger.LogWarning(ex,"Pixel query failed"); }
        finally { frame.Dispose(); }
    }
    public void Dispose()
    {
        if(_disposed) return; _disposed=true; _timer.Stop(); _stop.Cancel();
        foreach(var item in _measurements.ToArray()) item.Dispose();
        _measurements.Clear(); _states.Clear(); FrameCommitted=null;
    }
}
internal enum QueryKind { Pixel, Line, Region }
internal sealed record QueryRequest(object Geometry,QueryKind Kind,PixelCoordinate[]? Coordinates,PixelRegion? Region,Action<PixelSample[]?,RegionStatistics?> Publish);
internal interface IFrameMeasurement : IDisposable
{
    QueryRequest? Capture(FrameDescriptor descriptor);
    void ClearResult();
}
