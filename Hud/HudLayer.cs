using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fizzy.ImageViewer.Hud
{
    /// <summary>
    /// Layer 2: 屏幕坐标系 HUD 层
    /// 不随图片缩放移动
    /// </summary>
    internal class HudLayer : UserControl
    {
        private readonly StackPanel _topLeftPanel;
        private readonly Canvas _absoluteCanvas;
        private readonly TextBlock _pixelInfoText;
        private readonly Border _pixelInfoBorder;
        private readonly TextBlock _labelText;

        // 缓存上一次的显示字符串，避免重复分配相同内容
        private string? _lastPixelInfoString;

        public HudLayer()
        {
            var grid = new Grid
            {
                IsHitTestVisible = false
            };

            // 左上角面板（默认 anchor=null 时使用）
            _topLeftPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10)
            };
            grid.Children.Add(_topLeftPanel);

            // 绝对定位画布（anchor 非 null 时使用）
            _absoluteCanvas = new Canvas
            {
                IsHitTestVisible = false
            };
            grid.Children.Add(_absoluteCanvas);

            // 右上角标签
            _labelText = new TextBlock
            {
                FontSize = 35,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Opacity = 0.9
                }
            };
            grid.Children.Add(_labelText);

            // Clip in an independently measured canvas so long RGBA text cannot
            // increase the window's desired width or wrap in a narrow viewport.
            _pixelInfoText = new TextBlock
            {
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.NoWrap,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            _pixelInfoBorder = new Border
            {
                Child = _pixelInfoText,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            var pixelInfoCanvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            Canvas.SetLeft(_pixelInfoBorder, 10);
            Canvas.SetBottom(_pixelInfoBorder, 10);
            pixelInfoCanvas.Children.Add(_pixelInfoBorder);
            grid.Children.Add(pixelInfoCanvas);

            Content = grid;
        }

        /// <summary>
        /// 创建 HUD 文本并添加到左上角 StackPanel。
        /// </summary>
        public TextBlock AddText(string text, Brush color, double fontSize = 14)
        {
            var textBlock = CreateHudTextBlock(text, color, fontSize);
            _topLeftPanel.Children.Add(textBlock);
            return textBlock;
        }

        /// <summary>
        /// 创建 HUD 文本并放置在指定的屏幕坐标位置。
        /// </summary>
        /// <param name="text">文本内容</param>
        /// <param name="color">文本颜色</param>
        /// <param name="fontSize">字体大小</param>
        /// <param name="anchor">屏幕坐标锚点</param>
        /// <param name="alignment">锚点在文本上的对齐位置</param>
        public TextBlock AddTextAt(string text, Brush color, double fontSize, Point anchor, AnchorAlignment alignment)
        {
            var textBlock = CreateHudTextBlock(text, color, fontSize);

            // 先按 TopLeft 放置，在 SizeChanged 时根据 alignment 调整
            Canvas.SetLeft(textBlock, anchor.X);
            Canvas.SetTop(textBlock, anchor.Y);

            var (pivotX, pivotY) = AlignmentToPivot(alignment);

            // 初始定位 + SizeChanged 回调（当文本长度变化时重新对齐）
            textBlock.SizeChanged += (_, _) =>
            {
                Canvas.SetLeft(textBlock, anchor.X - textBlock.ActualWidth * pivotX);
                Canvas.SetTop(textBlock, anchor.Y - textBlock.ActualHeight * pivotY);
            };

            _absoluteCanvas.Children.Add(textBlock);
            return textBlock;
        }

        /// <summary>
        /// 更新已有 TextBlock 的文本和颜色。
        /// </summary>
        public void UpdateText(TextBlock tb, string text, Brush color)
        {
            tb.Text = text;
            tb.Foreground = color;
        }

        /// <summary>
        /// 从其父容器中移除 TextBlock。
        /// </summary>
        public void RemoveText(TextBlock tb)
        {
            _topLeftPanel.Children.Remove(tb);
            _absoluteCanvas.Children.Remove(tb);
        }

        /// <summary>
        /// 右上角标签文本。设为 null 或空字符串时隐藏。
        /// </summary>
        public string? Label
        {
            get => _labelText.Text;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _labelText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _labelText.Text = value;
                    _labelText.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// 更新像素信息显示，尽量 0-GC 实现。
        /// 仅当内容变化时才分配新 string。
        /// </summary>
        /// <param name="text">格式化后的像素信息</param>
        public void UpdatePixelInfo(ReadOnlySpan<char> text)
        {
            // 检查内容是否与上次相同
            if (_lastPixelInfoString != null &&
                _lastPixelInfoString.Length == text.Length &&
                text.SequenceEqual(_lastPixelInfoString.AsSpan()))
            {
                // 内容相同，无需更新
                return;
            }

            // 内容变化，创建新字符串
            _lastPixelInfoString = new string(text);
            _pixelInfoText.Text = _lastPixelInfoString;
        }

        /// <summary>
        /// 设置像素信息的可见性。
        /// </summary>
        public void SetPixelInfoVisible(bool visible)
        {
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (_pixelInfoBorder.Visibility != visibility) _pixelInfoBorder.Visibility = visibility;
        }

        /// <summary>
        /// 创建标准 HUD 样式的 TextBlock。
        /// </summary>
        private static TextBlock CreateHudTextBlock(string text, Brush color, double fontSize)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = color,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 1,
                    Opacity = 0.8
                },
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        /// <summary>
        /// 将 AnchorAlignment 转换为归一化的 pivot 系数 (0/0.5/1, 0/0.5/1)。
        /// </summary>
        private static (double X, double Y) AlignmentToPivot(AnchorAlignment alignment)
        {
            return alignment switch
            {
                AnchorAlignment.TopLeft => (0, 0),
                AnchorAlignment.TopCenter => (0.5, 0),
                AnchorAlignment.TopRight => (1, 0),
                AnchorAlignment.CenterLeft => (0, 0.5),
                AnchorAlignment.Center => (0.5, 0.5),
                AnchorAlignment.CenterRight => (1, 0.5),
                AnchorAlignment.BottomLeft => (0, 1),
                AnchorAlignment.BottomCenter => (0.5, 1),
                AnchorAlignment.BottomRight => (1, 1),
                _ => (0, 0)
            };
        }
    }
}
