using Microsoft.Extensions.ObjectPool;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Threading;

namespace Fizzy.ImageViewer.Internal;

/// <summary>
/// 刷新状态对象，用于跨线程传递渲染请求数据。
/// <para>
/// 该类与 <see cref="RefreshStatePool"/> 配合使用，实现对象池模式，
/// 避免每帧渲染时的堆分配。
/// </para>
/// </summary>
internal class RefreshState
{
    /// <summary>
    /// 图像源。在提交前已调用 Retain()，处理完成后需调用 Release()。
    /// </summary>
    public IImageSource? Source;
    
    /// <summary>
    /// 取消令牌，用于在渲染前检查请求是否已被取消。
    /// </summary>
    public CancellationToken Token;

    /// <summary>
    /// 重置状态以便重用。在归还到池之前调用。
    /// </summary>
    public void Reset()
    {
        Source = null;
        Token = default;
    }
}

/// <summary>
/// RefreshState 对象池，避免每帧分配。
/// </summary>
internal class RefreshStatePool
{
    private readonly ObjectPool<RefreshState> _pool = 
        new DefaultObjectPool<RefreshState>(
            new RefreshStatePolicy(),
            maximumRetained: Environment.ProcessorCount * 2);

    public RefreshState Rent()
    {
        return _pool.Get();
    }

    public void Return(RefreshState state)
    {
        state.Reset();
        _pool.Return(state);
    }

    /// <summary>
    /// ObjectPool 策略类，定义对象的创建和归还逻辑。
    /// </summary>
    private sealed class RefreshStatePolicy : IPooledObjectPolicy<RefreshState>
    {
        public RefreshState Create()
        {
            return new RefreshState();
        }

        public bool Return(RefreshState obj)
        {
            // 验证对象状态：应该已经被 Reset() 清理过了
            // 这里做二次检查，确保对象可以安全复用
            if (obj.Source != null || obj.Token != default)
            {
                // 对象状态异常，不归还到池（会被 GC）
                return false;
            }

            // 对象状态正常，可以复用
            return true;
        }
    }
}
