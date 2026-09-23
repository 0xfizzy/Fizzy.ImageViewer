using Fizzy.ImageViewer.Imaging.Queries;
using System.Windows;

namespace Fizzy.ImageViewer.Measurements.Methods;

internal sealed class LineStrengthTool(IMeasurementContext context) : LineTool(context)
{
    public override string Id => MeasureToolIds.LineStrength;
    public override string DisplayName => "Line strength";
    private protected override MeasurementItem CreateItem(IMeasurementContext context, Point start) => new LineStrengthItem(context, start);

    private sealed class LineStrengthItem : MeasurementItem
    {
        private QueryRequest? _cached;
        private (long Version, Frames.FrameDescriptor Descriptor)? _cachedGeometry;
        private Imaging.LineProfile _profile = new();
        private LineProfilePlotView? _plotView;
        public LineStrengthItem(IMeasurementContext context, Point start)
            : base(context, MeasurementGeometry.Line(start, start), Shapes.CreateLine(context.Style), Shapes.CreateLabel(start, "", 5, 0, context.Style)) { }
        protected override void OnComplete()
        {
            _plotView = new LineProfilePlotView();
            OwnWindow(_plotView.Window);
            _plotView.Window.Show();
            Subscribe();
        }
        public override QueryRequest? Capture(Frames.FrameDescriptor descriptor)
        {
            if (IsDisposed || !IsComplete) return null;
            var geometry = (Geometry.Version, descriptor);
            if (_cachedGeometry == geometry) return _cached;
            _cachedGeometry = geometry;
            var profile = new Imaging.LineProfile();
            var points = profile.Prepare(descriptor, Geometry.Start.X, Geometry.Start.Y, Geometry.End.X, Geometry.End.Y);
            return _cached = new LineProfileQueryRequest(new(Id, Geometry.Version), points, samples =>
            { profile.Apply(samples); _profile = profile; Result = profile; Publish(); });
        }
        public override void ClearResult()
        {
            base.ClearResult();
            if (ResultWindow != null) _plotView?.Clear();
        }
        private void Publish()
        {
            if (IsDisposed || ResultWindow == null) return;
            _plotView!.ShowProfile(_profile);
        }
    }
}
