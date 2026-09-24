using Fizzy.ImageViewer.Measurements.Presentation;
using Fizzy.ImageViewer.Layers;
using Fizzy.ImageViewer.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer
{
    internal class ViewerWindow : Window
    {
        // 公开图层供 Manager 使用
        public ImageLayer ImageLayer { get; }
        internal MeasurementOverlay MeasurementOverlay => Layers.Measurements.Overlay;
        public HudLayer HudLayer { get; }
        public Layers.ViewerLayers Layers { get; }

        public ViewerWindow(string title) : this(title, new ViewerLifetime()) { }

        internal ViewerWindow(string title, ViewerLifetime lifetime)
        {
            // === Window 属性 ===
            Title = title;
            Width = 800;
            Height = 600;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // === 实例化图层 ===
            ImageLayer = new ImageLayer();
            Layers = new Layers.ViewerLayers(ImageLayer.TransformGroup, lifetime);
            HudLayer = new HudLayer();


            ImageLayer.ScaleChanged += Layers.Collection.UpdateScale;

            var grid = new Grid();

            // 叠加顺序很重要：0在底，2在顶
            grid.Children.Add(ImageLayer);
            grid.Children.Add(Layers.Collection.Root);
            grid.Children.Add(HudLayer);

            Content = grid;
        }

        // 允许外部强制关闭
        public bool CanUserClose { get; set; } = true;
        private bool _programmaticClose;
        internal Exception? ClosingError { get; private set; }
        internal void CloseProgrammatically()
        {
            _programmaticClose = true;
            try { Close(); }
            finally { _programmaticClose = false; }
        }

        // 无边框模式
        public bool Borderless
        {
            get => WindowStyle == WindowStyle.None;
            set
            {
                if (value)
                {
                    WindowStyle = WindowStyle.None;
                    ResizeMode = ResizeMode.NoResize;
                }
                else
                {
                    WindowStyle = WindowStyle.SingleBorderWindow;
                    ResizeMode = ResizeMode.CanResize;
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!CanUserClose && !_programmaticClose)
            {
                e.Cancel = true;
            }
            try { base.OnClosing(e); }
            catch (Exception ex) when (_programmaticClose) { ClosingError = ex; }
            finally
            {
                // Disposal owns the window lifetime; subscribers cannot veto it.
                if (_programmaticClose) e.Cancel = false;
                else if (!CanUserClose) e.Cancel = true;
            }
        }
    }
}
