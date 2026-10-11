# 自定义测量工具

[English](../en/measurement-tools.md)

应用需要自己的交互创建流程时，实现测量工具。已有功能足够时优先使用[内置测量](measurements.md)。非交互标记使用[绘制层](drawing-layers.md)；没有独立创建测量或恢复已保存 ROI 的 API。

## 创建与注册工具

以下完整工具创建矩形 ROI，并复用框架的编辑、统计、标签和通知。每次激活都会创建一个新会话：

```csharp
using Fizzy.ImageViewer.Measurements;
using System.Windows;

public sealed class CustomRoi : IMeasurementTool
{
    public string Id => "custom-roi";
    public string DisplayName => "Custom ROI";
    public IMeasurementToolSession CreateSession(IMeasurementToolContext context)
        => new Session(context);

    private sealed class Session(IMeasurementToolContext context) : IMeasurementToolSession
    {
        private IMeasurement? _preview;
        private Point _start;

        public MeasurementClickResult OnClick(Point point)
        {
            if (_preview is null or { IsDisposed: true })
            {
                _start = point;
                _preview = context.CreateMeasurement(
                    MeasurementGeometry.Rectangle(point, point),
                    new() { Query = MeasurementQueryKind.RegionStatistics });
                return MeasurementClickResult.Continue;
            }
            OnMouseMove(point);
            _preview.Complete();
            return MeasurementClickResult.Finish;
        }

        public void OnMouseMove(Point point)
        {
            if (_preview is { IsDisposed: false })
                _preview.UpdateGeometry(MeasurementGeometry.Rectangle(_start, point));
        }

        // The framework releases unfinished measurements after cancellation.
        public void Cancel() { }
        public void Dispose() { }
    }
}
```

在功能安装期间保留注册句柄：

```csharp
using var registration = viewer.RegisterMeasurementTool(new CustomRoi());
viewer.ActivateMeasurementTool("custom-roi");
```

查询选项见[几何与查询对应表](measurements.md#查询与保留结果)。对需要保留的每一项调用 `Complete()`；仅返回 `Finish` 不会完成测量。

## 注册所有权

`RegisterMeasurementTool` 返回只代表该次注册的 `IDisposable` 句柄。可从任意线程幂等释放，Viewer 关闭后也安全。撤销取消该注册的活动会话，保留已完成测量；调用方仍拥有工具对象，Viewer 不释放它。不保留句柄时，注册持续到全局撤销或 Viewer 关闭。

`Id` 和 `DisplayName` 在 Viewer STA 上读取一次。两者必须非空，ID 按序号比较且区分大小写；重复 ID 被拒绝。读取外部 getter 后会再次检查生命周期；若 getter 重入关闭 Viewer，则拒绝注册并抛出 `ObjectDisposedException`。

`UnregisterMeasurementTool(id)` 删除当前同 ID 注册并返回是否删除。组件清理应释放自己的句柄，避免误删同名替代注册。撤销先于取消回调生效；回调失败在清理后传播，不恢复注册。

激活在打断旧会话前固定注册实例，无效初始请求保留当前交互。重入可替代请求，旧请求不会激活同 ID 的新注册。菜单打开时捕获注册；点击时若已撤销、隐藏、禁用命中或正在清空，则忽略该动作。注册变化在后续菜单打开时生效。

## 会话与完成

工具实现 `IMeasurementTool` 的 `Id`、`DisplayName` 和 `CreateSession(IMeasurementToolContext)`。每次激活在 Viewer STA 调用工厂，并要求返回新的 `IMeasurementToolSession`。预览句柄、首击位置等可变数据属于会话；工具只保存可复用配置。跨 Viewer 共享工具时，工厂及配置必须线程安全。

会话接收 `OnClick(Point)`、`OnMouseMove(Point)` 和 `Cancel()`。点击返回 `Continue` 继续输入，返回 `Finish` 结束输入；未完成测量自动释放。正常完成不调用 `Cancel`；中断先结束上下文，再调用一次 `Cancel`，即使取消失败也清理预览。每个返回的会话都会在 STA 恰好释放一次，包括完成、回调失败和工厂被替代的情况。会话资源放在 `Dispose` 清理，`Cancel` 只处理取消特有行为。

工厂可以创建预览和重入 Viewer API。工厂返回时若该激活已被替代，返回的会话会被取消，不影响新会话。工厂失败或返回 null 会释放它创建的未完成测量；尚未转交给测量的资源由工厂负责清理。

异步工作可在完成需要保留的测量后调用 `context.Finish()`。它调度到 STA，只结束所属激活；已结束、被替代或 Viewer 已关闭时返回 false，不结束替代会话。`IMeasurement.Complete()` 只完成一个项并释放其上下文引用，不结束工具。上下文、测量和事件快照通过 `Origin` 保留 `ToolId` 与 `SessionId`；同次激活的多个项共享会话 ID，后续激活使用新 ID。

## 重入、取消与失败

普通操作中，工具回调或完成通知同步启动的新会话优先，即使工具相同。旧回调返回或抛出异常不会取消替代会话或恢复旧输入状态；取消打断外层启动时也遵循该规则。清理只在仍拥有该会话时恢复空闲状态。

显式 API 调用传播工具回调异常；框架输入和曲线窗口回调记录错误。操作及清理同时失败时，聚合异常先保留原始失败，再保留清理失败；仍尝试全部安全清理。完成、删除和其他通知订阅者的异常逐个隔离，不中断后续订阅者。

取消和正常完成都先结束上下文，再清理未完成项；已完成项由测量集合持有至删除。旧上下文不能再创建测量，否则抛出 `ObjectDisposedException`。旧清理不能删除新会话创建的测量，即使新会话由释放回调启动。取消中撤销注册也会使外层针对该注册的激活失效，同名重新注册不会恢复旧请求。

显式调用在清理后传播会话和预览的组合失败；框架输入记录错误，关闭则将它们纳入 `DisposeAsync`。批量清理期间拒绝新测量和会话，重入清理保持幂等；关闭开始后激活抛出 `ObjectDisposedException`，清理全部完成项与预览并等待查询。

## 资源与线程

工具回调、上下文创建和通知运行在 Viewer STA。`IMeasurement` 句柄会调度读取、更新和订阅，因此后台工作可以更新或完成仍存活的预览。取消释放预览后，晚到的更新或完成抛出 `ObjectDisposedException`。关闭后可安全释放和退订，`Id`、`Origin`、`IsDisposed` 仍可读；其他访问需要 Viewer 存活。不要在 STA 回调中同步等待会调用这些句柄的工作线程。

用 `AddResource` 和 `OnDispose` 将资源及订阅绑定到测量。`AddResource` 仅成功后转移所有权；失败时新资源仍归调用方，重复注册同一资源被拒绝且不改变原有所有权。不得在多个测量之间共享被拥有的资源。工具撤销保留完成项；隐藏测量层取消预览而保留完成项，`Viewer.Hide()` 保留会话和预览，清空或关闭删除所有项。

释放依次撤销查询、执行清理回调、释放资源、关闭曲线和移除视觉；任一步失败后仍尝试后续安全步骤。显式释放和清空聚合清理错误，框架输入记录错误，Viewer 关闭将资源错误纳入共享 `DisposeAsync`。重复释放不会重试已执行的资源清理。`FrameCommitted` 是 Viewer 通知，不是查询执行回调。

继续阅读[测量结果](measurements.md)及 [Viewer 生命周期](public-api.md)。
