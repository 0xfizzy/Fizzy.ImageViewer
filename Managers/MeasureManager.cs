using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Interfaces;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Fizzy.ImageViewer.Managers
{
    public class MeasureManager : IDisposable
    {
        private readonly ImageLayer _inputLayer;
        private readonly OverlayLayer _outputLayer;
        private readonly MeasureContext _context;
        private readonly Dictionary<string, IMeasureMethod> _registeredMethods = [];

        // 状态标识
        private bool _hasSelection = false;
        private IMeasureMethod? _activeMethod;

        public IReadOnlyDictionary<string, IMeasureMethod> RegisteredMethods => _registeredMethods;
        public bool HasSelection => _hasSelection;

        /// <summary>
        /// 获取 MeasureContext，供外部访问图像更新事件。
        /// </summary>
        public MeasureContext Context => _context;

        public MeasureManager(ImageLayer inputLayer, OverlayLayer outputLayer, Func<Fizzy.ImageViewer.Frames.FrameLease?> acquire, Microsoft.Extensions.Logging.ILogger logger)
        {
            _inputLayer = inputLayer;
            _outputLayer = outputLayer;
            _context = new MeasureContext(outputLayer, acquire, logger);

            _inputLayer.ImageMouseDown += OnMouseDown;
            _inputLayer.ImageMouseMove += OnMouseMove;
        }
        
        /// <summary>
        /// 通知图像已更新。由 Viewer 调用。
        /// </summary>
        internal void NotifyFrameCommitted(Fizzy.ImageViewer.Frames.FrameInfo info)
        {
            _context.NotifyFrameCommitted(info);
        }

        public Task Completion => _context.Completion;
        public void Dispose() { _inputLayer.ImageMouseDown -= OnMouseDown; _inputLayer.ImageMouseMove -= OnMouseMove; CancelCurrent(); _context.Dispose(); }
        public void RegisterMethod(IMeasureMethod method)
        {
            _registeredMethods[method.Name] = method;
        }

        public void Start(string methodName)
        {
            CancelCurrent(); // 确保之前的状态被清理

            if (_registeredMethods.TryGetValue(methodName, out var method))
            {
                _activeMethod = method;
                _inputLayer.Container.Cursor = Cursors.Pen;
                _hasSelection = true;
                _outputLayer.SetHitTestEnabled(false); // 测量时关闭选择
            }
        }

        // 公开给外部 (如 CancelMenuItem) 使用
        public void Cancel()
        {
            CancelCurrent();
        }

        private void CancelCurrent()
        {
            if (_activeMethod != null)
            {
                _activeMethod.Cancel(_context);
                _activeMethod = null;
                _inputLayer.Container.Cursor = Cursors.Cross;
                _outputLayer.SetHitTestEnabled(true); // 恢复选择
            }
            _hasSelection = false;
        }

        private void OnMouseDown(double imageX, double imageY)
        {
            if (_activeMethod == null) return;

            var imagePoint = new Point(imageX, imageY);
            bool finished = _activeMethod.OnClick(imagePoint, _context);
            if (finished)
            {
                _activeMethod = null;
                _inputLayer.Container.Cursor = Cursors.Cross;
                _hasSelection = false;
                _outputLayer.SetHitTestEnabled(true); // 测量完成，恢复选择
            }
        }

        private void OnMouseMove(double imageX, double imageY)
        {
            if (_activeMethod == null) return;
            _activeMethod.OnMouseMove(new Point(imageX, imageY), _context);
        }
    }
}
