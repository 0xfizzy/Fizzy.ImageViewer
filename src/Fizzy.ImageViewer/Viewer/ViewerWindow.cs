using System.Windows;
using System.Windows.Media;

namespace Fizzy.ImageViewer
{
    internal class ViewerWindow : Window
    {
        internal ImageViewerControl Control { get; }

        internal ViewerWindow(string title, ImageViewerControl control)
        {
            Title = title;
            Width = 800;
            Height = 600;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Control = control;
            Content = control;
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
