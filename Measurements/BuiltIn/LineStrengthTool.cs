using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using Fizzy.ImageViewer.Imaging.Queries;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.BuiltIn;

internal sealed class LineStrengthTool(IMeasurementContext context) : LineTool(context)
{
    public override string Id => MeasurementToolIds.LineStrength;
    public override string DisplayName => "Line strength";
    private protected override MeasurementItem CreateItem(IMeasurementContext context, Point start) => new LineStrengthItem(context, start);

    private sealed class LineStrengthItem : MeasurementItem, IFrameQueryClient
    {
        private QueryRequest? _cached;
        private (long Version, Frames.FrameDescriptor Descriptor)? _cachedGeometry;
        private QuerySubscription? _subscription;
        private LineProfilePlotView? _plotView;
        public LineStrengthItem(IMeasurementContext context, Point start)
            : base(context, MeasurementGeometry.Line(start, start), Shapes.CreateLine(context.Style), Shapes.CreateLabel(start, "", 5, 0, context.Style)) { }
        protected override void OnComplete()
        {
            _plotView = new LineProfilePlotView();
            _plotView.Window.Closed += PlotClosed;
            _plotView.Window.Show();
            if (!IsDisposed) _subscription = Context.Register(this);
        }
        public QueryRequest? Capture(Frames.FrameDescriptor descriptor)
        {
            if (IsDisposed || !IsComplete) return null;
            var geometry = (Geometry.Version, descriptor);
            if (_cachedGeometry == geometry) return _cached;
            _cachedGeometry = geometry;
            var profile = new Imaging.LineProfile();
            var points = profile.Prepare(descriptor, Geometry.Start.X, Geometry.Start.Y, Geometry.End.X, Geometry.End.Y);
            return _cached = new LineProfileQueryRequest(new(Id, Geometry.Version), points, samples =>
            { if (IsDisposed) return; profile.Apply(samples); _plotView?.ShowProfile(profile); });
        }
        protected override void OnGeometryChanged() => ClearResult();
        public void ClearResult()
        {
            UpdateText();
            _plotView?.Clear();
        }
        private void PlotClosed(object? sender, EventArgs args) => Dispose();
        protected override void OnDisposing()
        {
            try { _subscription?.Dispose(); }
            finally
            {
                var plot = _plotView; _plotView = null;
                _cached = null; _cachedGeometry = null;
                if (plot != null)
                {
                    plot.Window.Closed -= PlotClosed;
                    plot.Window.Close();
                }
            }
        }
    }
}
