using Fizzy.ImageViewer.Constants;
using Fizzy.ImageViewer.Interfaces;
using Fizzy.ImageViewer.Internal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fizzy.ImageViewer;

public partial class Viewer
{
    // === 渲染状态 (0-GC 优化) ===
    
    /// <summary>
    /// 当前用于渲染的 WriteableBitmap，在 UI 线程创建和访问。
    /// </summary>
    private WriteableBitmap? _wBitmap;
    
    /// <summary>
    /// 当前排队中的渲染请求数量。使用 Interlocked 保证线程安全。
    /// </summary>
    private int _renderingCount = 0;
    
    /// <summary>
    /// 渲染队列最大深度的后备字段。
    /// </summary>
    private int _maxRenderQueue = RenderingConstants.DefaultMaxRenderQueue;
    
    /// <summary>
    /// RefreshState 对象池，避免每帧分配。
    /// </summary>
    private readonly RefreshStatePool _statePool = new();
    
    /// <summary>
    /// 预分配的 UI 线程回调委托，避免每次 BeginInvoke 时创建新委托。
    /// </summary>
    private SendOrPostCallback? _refreshCallback;

    /// <summary>
    /// 帧冻结标志。当为 true 时，RefreshAsync 会丢弃所有新帧。
    /// 用于右键菜单打开时冻结当前帧，确保保存操作获得准确的帧。
    /// </summary>
    private volatile bool _isFrozen;

    /// <summary>
    /// 冻结帧更新，丢弃所有新输入帧。
    /// </summary>
    internal void Freeze() => _isFrozen = true;

    /// <summary>
    /// 解冻帧更新，恢复正常渲染。
    /// </summary>
    internal void Unfreeze() => _isFrozen = false;

    /// <summary>
    /// 刷新图像显示。0-GC 实现，接受跳帧。
    /// <para>
    /// 生命周期契约：调用者传入的 source 的所有权转移给 Viewer。
    /// Viewer 保证在所有路径上调用 Release()（渲染完成、跳帧、冻结）。
    /// </para>
    /// </summary>
    /// <typeparam name="TSource">图像源类型，使用泛型约束避免 struct 装箱</typeparam>
    /// <param name="source">图像源，所有权转移给 Viewer</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>ValueTask，立即返回不等待渲染完成</returns>
    public ValueTask RefreshAsync<TSource>(TSource source, CancellationToken ct = default)
        where TSource : IImageSource
    {
        // 窗口不存在，直接释放
        if (_window == null)
        {
            source.Dispose();
            return ValueTask.CompletedTask;
        }

        // 冻结状态下丢弃所有新帧
        if (_isFrozen)
        {
            source.Dispose();
            return ValueTask.CompletedTask;
        }

        // 先增加计数，再检查，避免竞态条件
        var count = Interlocked.Increment(ref _renderingCount);
        if (count > Interlocked.CompareExchange(ref _maxRenderQueue, 0, 0))
        {
            Interlocked.Decrement(ref _renderingCount);
            source.Dispose(); // 跳帧时释放
            return ValueTask.CompletedTask;
        }

        // 使用泛型包装器避免 struct 装箱
        // 注意：不再调用 Retain，因为调用者已经持有引用，所有权转移给 Viewer
        //var wrapper = ImageSourceWrapper<TSource>.Rent(source);

        // 从池中获取状态对象，避免堆分配
        var state = _statePool.Rent();
        state.Source = source;
        state.Token = ct;

        // 复用预分配回调，避免每次创建委托
        _refreshCallback ??= RefreshOnUIThread;

        // BeginInvoke 比 InvokeAsync 分配更少（不创建 DispatcherOperation）
        _window.Dispatcher.BeginInvoke(_refreshCallback, DispatcherPriority.Send, state);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// UI 线程上执行的渲染回调。
    /// </summary>
    private void RefreshOnUIThread(object? state)
    {
        var s = (RefreshState)state!;
        try
        {
            if (s.Token.IsCancellationRequested || s.Source == null) return;

            var source = s.Source;
            EnsureBitmap(source.Width, source.Height, source.WpfFormat);
            source.WriteTo(_wBitmap!);
        }
        finally
        {
            Interlocked.Decrement(ref _renderingCount);
            s.Source?.Dispose();
            _statePool.Return(s);
        }
    }

    /// <summary>
    /// 确保 WriteableBitmap 存在且尺寸/格式匹配。
    /// 如果不匹配则重新创建。
    /// </summary>
    private void EnsureBitmap(int width, int height, PixelFormat format)
    {
        if (_wBitmap == null ||
            _wBitmap.PixelWidth != width ||
            _wBitmap.PixelHeight != height ||
            _wBitmap.Format != format)
        {
            _wBitmap = new WriteableBitmap(width, height, 
                RenderingConstants.DefaultDpi, RenderingConstants.DefaultDpi, format, null);
            _window.Layer0.SetImage(_wBitmap);
            _window.Layer0.FitImageToContainer();
        }
        
        // 通知 MeasureManager 图像已更新
        _measureManager?.NotifyImageUpdated(_wBitmap);
    }
}
