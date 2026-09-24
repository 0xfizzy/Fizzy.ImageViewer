using Fizzy.ImageViewer.Interaction;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fizzy.ImageViewer.Viewport
{
    /// <summary>
    /// Layer 0: 负责图片渲染、底层鼠标交互、坐标变换矩阵维护。
    /// <para>
    /// 该层是图像显示的基础层，提供：
    /// <list type="bullet">
    /// <item>图像的缩放和平移变换</item>
    /// <item>鼠标滚轮缩放、中键拖拽平移</item>
    /// <item>双击恢复默认视图</item>
    /// <item>图像坐标与容器坐标的相互转换</item>
    /// </list>
    /// </para>
    /// </summary>
    internal class ImageViewport : UserControl
    {
        // === UI 组件 ===

        /// <summary>
        /// 图像容器 Canvas，用于接收鼠标事件。
        /// </summary>
        public Canvas Container { get; }

        /// <summary>
        /// 图像显示控件。
        /// </summary>
        public Image ImageDisplay { get; }

        // === 变换组件 (供外部绑定) ===

        /// <summary>
        /// 变换组，包含缩放和平移变换。
        /// </summary>
        public TransformGroup TransformGroup { get; }

        /// <summary>
        /// 缩放变换。
        /// </summary>
        public ScaleTransform Scaler { get; }

        /// <summary>
        /// 平移变换。
        /// </summary>
        public TranslateTransform Panner { get; }

        // === 交互状态 ===
        private readonly ViewportPan _pan;

        // === 事件 (使用 Action 避免 struct 装箱) ===

        /// <summary>
        /// 缩放比例变化时触发。参数为新的缩放比例。
        /// </summary>
        public event Action<double>? ScaleChanged;

        /// <summary>
        /// 鼠标在图像上按下时触发。参数为图像坐标 (x, y)。
        /// </summary>
        public event Action<double, double>? ImageMouseDown;

        /// <summary>
        /// 鼠标在图像上移动时触发。参数为图像坐标 (x, y)。
        /// </summary>
        public event Action<double, double>? ImageMouseMove;

        public ImageViewport()
        {
            // 1. 初始化变换
            Scaler = new ScaleTransform();
            Panner = new TranslateTransform();
            TransformGroup = new TransformGroup();
            TransformGroup.Children.Add(Scaler);
            TransformGroup.Children.Add(Panner);

            // 2. 初始化图片控件
            ImageDisplay = new Image
            {
                Stretch = Stretch.Fill,
                RenderTransform = TransformGroup, // 变换应用在图片上
                IsHitTestVisible = false          // 图片本身不拦截，由 Canvas 拦截
            };

            // 3. 初始化容器
            Container = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent, // 必须透明以接收点击
                Cursor = Cursors.Cross,
                ClipToBounds = true
            };
            Container.Children.Add(ImageDisplay);
            _pan = new ViewportPan(new ElementMouseCapture(Container));

            Content = Container;

            // 4. 绑定交互事件
            SetupInteraction();
        }

        public void SetImage(ImageSource source, int pixelWidth, int pixelHeight)
        {
            ImageDisplay.Source = source;
            ImageDisplay.Width = pixelWidth;
            ImageDisplay.Height = pixelHeight;
        }

        private void SetupInteraction()
        {
            Container.MouseWheel += OnMouseWheel;
            Container.MouseLeftButtonDown += OnMouseLeftDown;
            Container.MouseDown += OnMouseDown;
            Container.MouseMove += OnMouseMove;
            Container.MouseUp += OnMouseUp;
            Container.MouseLeave += (s, e) => EndPan();
            Container.LostMouseCapture += (s, e) => _pan.LostCapture();
            Container.IsVisibleChanged += (s, e) => { if (!Container.IsVisible) EndPan(); };
            Container.Unloaded += (s, e) => EndPan();
            Container.SizeChanged += (s, e) => FitToViewport();
        }

        // === 视口交互 ===

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var point = e.GetPosition(ImageDisplay); // 获取相对于图片的坐标
            DoScale(point, e.Delta);
        }

        private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            // 双击恢复默认缩放和位移
            if (e.ClickCount == 2)
            {
                FitToViewport();
                return;
            }

            // 仅在有订阅者时计算坐标（避免无用计算）
            var handler = ImageMouseDown;
            if (handler == null) return;

            var posContainer = e.GetPosition(Container);
            var posImage = ContainerToImage(posContainer);
            handler(posImage.X, posImage.Y);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _pan.Begin(e.GetPosition(Container));
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var posContainer = e.GetPosition(Container);

            // 1. 中键拖拽逻辑 (必须使用容器坐标，因为是在操作容器的视口)
            if (_pan.IsActive)
            {
                DoMove(posContainer);
            }

            // 2. 仅在有订阅者时计算图像坐标并触发（避免无用计算和装箱）
            var handler = ImageMouseMove;
            if (handler != null)
            {
                var posImage = ContainerToImage(posContainer);
                handler(posImage.X, posImage.Y);
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                EndPan();
            }

        }

        internal void EndPan() => _pan.End();

        // === 数学逻辑 ===

        /// <summary>
        /// 执行以指定点为中心的缩放操作。
        /// </summary>
        /// <param name="point">缩放中心点（图像坐标）</param>
        /// <param name="delta">滚轮增量，正值放大，负值缩小</param>
        private void DoScale(Point point, double delta)
        {
            var sX = Scaler.ScaleX;
            var sY = Scaler.ScaleY;

            if (delta > 0)
            {
                sX *= ViewportConstants.ScaleFactor;
                sY *= ViewportConstants.ScaleFactor;
            }
            else if (delta < 0)
            {
                sX /= ViewportConstants.ScaleFactor;
                sY /= ViewportConstants.ScaleFactor;
            }

            // 限制缩放范围
            if (sX < ViewportConstants.MinScale || sX > ViewportConstants.MaxScale)
                return;

            var dX = point.X * (Scaler.ScaleX - sX);
            var dY = point.Y * (Scaler.ScaleY - sY);

            Scaler.ScaleX = sX;
            Scaler.ScaleY = sY;
            Panner.X += dX;
            Panner.Y += dY;

            ScaleChanged?.Invoke(sX);
        }

        /// <summary>
        /// 执行平移操作。
        /// </summary>
        /// <param name="moveEndPoint">当前鼠标位置（容器坐标）</param>
        private void DoMove(Point moveEndPoint)
        {
            var delta = _pan.Move(moveEndPoint);
            Panner.X += delta.X;
            Panner.Y += delta.Y;
        }

        /// <summary>
        /// 将图像适配到容器大小，居中显示。
        /// </summary>
        public void FitToViewport()
        {
            if (ImageDisplay.Source == null || Container.ActualWidth == 0 || Container.ActualHeight == 0)
                return;

            double imgW = ImageDisplay.Width;
            double imgH = ImageDisplay.Height;
            double containerW = Container.ActualWidth;
            double containerH = Container.ActualHeight;

            double scale = Math.Min(containerW / imgW, containerH / imgH);
            Scaler.ScaleX = scale;
            Scaler.ScaleY = scale;

            double displayW = imgW * scale;
            double displayH = imgH * scale;
            Panner.X = (containerW - displayW) / 2;
            Panner.Y = (containerH - displayH) / 2;

            ScaleChanged?.Invoke(Scaler.ScaleX);
        }

        // === 坐标转换工具 ===

        /// <summary>
        /// 将容器坐标转换为图像像素坐标。
        /// </summary>
        /// <param name="containerPt">容器坐标</param>
        /// <returns>图像像素坐标</returns>
        public Point ContainerToImage(Point containerPt)
        {
            // TranslatePoint 会自动处理逆变换矩阵
            return Container.TranslatePoint(containerPt, ImageDisplay);
        }

        /// <summary>
        /// 将图像像素坐标转换为容器坐标。
        /// </summary>
        /// <param name="imagePt">图像像素坐标</param>
        /// <returns>容器坐标</returns>
        public Point ImageToContainer(Point imagePt)
        {
            return ImageDisplay.TranslatePoint(imagePt, Container);
        }
    }
}
