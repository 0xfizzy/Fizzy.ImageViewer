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
        public OverlayLayer Layer1 { get; }
        public HudLayer Layer2 { get; }

        public ViewerWindow(string title)
        {
            // === Window 属性 ===
            Title = title;
            Width = 800;
            Height = 600;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // === 实例化图层 ===
            Layer0 = new ImageLayer();
            Layer1 = new OverlayLayer();
            Layer2 = new HudLayer();

            Layer1.BindTransform(Layer0.TransformGroup);

            Layer0.ScaleChanged += scale =>
            {
                Layer1.UpdateScale(scale);
            };

            // 点击图像空白处（未命中 overlay 形状）时取消选中
            Layer0.ImageMouseDown += (_, _) =>
            {
                Layer1.ClearSelection();
            };

            var grid = new Grid();

            // 叠加顺序很重要：0在底，2在顶
            grid.Children.Add(Layer0);
            grid.Children.Add(Layer1);
            grid.Children.Add(Layer2);

            Content = grid;
        }

        // 允许外部强制关闭
        public bool CanUserClose { get; set; } = true;

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
            if (!CanUserClose)
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }
    }
}