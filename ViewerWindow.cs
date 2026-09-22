using Fizzy.ImageViewer.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fizzy.ImageViewer
{
    public class ViewerWindow : Window
    {
        // 公开图层供 Manager 使用
        public ImageLayer Layer0 { get; }
        internal OverlayLayer Layer1 { get; }
        public HudLayer Layer2 { get; }
        public Drawing.ViewerLayers Layers { get; }

        public ViewerWindow(string title) : this(title, new Internal.ViewerLifetime()) { }

        internal ViewerWindow(string title, Internal.ViewerLifetime lifetime)
        {
            // === Window 属性 ===
            Title = title;
            Width = 800;
            Height = 600;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // === 实例化图层 ===
            Layer0 = new ImageLayer();
            Layers = new Drawing.ViewerLayers(Layer0.TransformGroup, lifetime);
            Layer1 = Layers.MeasurementOverlay;
            Layer2 = new HudLayer();

            Layer1.BindTransform(Layer0.TransformGroup);

            Layer0.ScaleChanged += scale =>
            {
                Layer1.UpdateScale(scale);
                Layers.UpdateScale(scale);
            };

            // 点击图像空白处（未命中 overlay 形状）时取消选中
            Layer0.ImageMouseDown += (_, _) =>
            {
                Layer1.ClearSelection();
            };

            var grid = new Grid();

            // 叠加顺序很重要：0在底，2在顶
            grid.Children.Add(Layer0);
            grid.Children.Add(Layers.Root);
            grid.Children.Add(Layer2);

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
