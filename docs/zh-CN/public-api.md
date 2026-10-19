# Viewer 与生命周期

[English](../en/public-api.md)

本指南说明窗口配置和关闭责任。`Viewer` 为 sealed，应用适配器通过组合拥有它，不访问底层 WPF 窗口。[入门](getting-started.md)提供等待用户关闭的完整程序。

`IViewerCapabilities` 聚合借用的宿主无关查看能力、全局图层及停止观察；`IViewer` 在此基础上为拥有者增加异步释放。两种接口均由 `Viewer` 与 `ImageViewerControl` 实现；窗口操作通过仅由 `Viewer` 实现的 `IViewerWindow` 提供。控件生命周期与宿主关闭见[嵌入指南](embedding.md)。

## 配置与显示

```csharp
await using var viewer = new Fizzy.ImageViewer.Viewer(showWindow: false);
viewer.Title = "Inspection";
viewer.HudLabelText = "Ready";
viewer.Show();
// Keep the viewer alive for the owning feature's lifetime.
```

## 线程与窗口生命周期

构造函数的 `ILogger<Viewer>` 可省略或传入 null，此时使用 `NullLogger<Viewer>.Instance`；接入应用日志时再传入 logger。默认构造立即显示窗口。使用 `showWindow: false` 可以先配置、订阅事件和提交帧，再调用 `Show()`。隐藏窗口已有运行的 STA，即使从未显示，也必须由拥有者等待 `DisposeAsync()`。

窗口属性和操作同步调度到 Viewer STA，关闭开始后抛出 `ObjectDisposedException`。`Show` 显示、恢复最小化并请求激活；`Hide` 隐藏但保留帧、绘图和测量；`Minimize` 改变状态但不隐藏，也不隐式显示原本隐藏的窗口。`IsVisible` 遵循 WPF 可见性，已显示但最小化的窗口仍可见；`IsMinimized` 独立反映最小化状态。

`Visible` 读写窗口显隐设置；设为 true 只显示，不激活或解除最小化，`Show()` 才显式恢复并激活。隐藏或最小化时继续接收最新输入，但暂停新的显示工作；恢复后显示最新帧。`IsVisible` 不检测遮挡，移出屏幕、被覆盖或失去焦点不会暂停更新。

`DisposeAsync` 关闭窗口，等待所属后台工作与 STA 停止。重复调用观察同一个完成任务及其固定成功或失败结果。资源清理失败在其他安全清理和真实 STA 退出后报告；通知订阅异常记录并隔离。`Closed` 的订阅在终止通知交付后释放。Viewer 只对应一次窗口生命周期，关闭后不能重新显示，应创建新实例。

构造时在启动 STA 前验证窗口边界。初始化失败在所属线程执行安全清理并排空后台工作，然后重新抛出原始异常；清理也失败时，聚合保留两类错误。无法证明安全分离的 native 资源保持隔离，不释放或复用；关闭任务失败不证明资源全部释放。

应用适配器应在显示前注册所需资源，适配器初始化失败时也释放 Viewer。用户关闭通知与显式适配器释放汇入同一个幂等清理任务：释放应用资源并等待 Viewer 的 `DisposeAsync`。转发测量删除事件的适配器应保留订阅直到窗口清理结束。

`Closed` 只报告窗口关闭，不代表渲染、查询或窗口线程结束；没有独立的公共 `Close` 方法。Viewer 回调运行在其 STA，不得同步等待关闭或依赖该 Dispatcher 的工作。

关闭后的帧提交仍立即接管帧并返回 `Closed`，不采用窗口属性的异常策略。绘图和 HUD 句柄关闭后仍可幂等释放。帧排空与租约责任见[帧与像素](frame-pipeline.md)，绘图句柄见[绘制](drawing-layers.md)。

`HudLabelText` 控制右上文字；`IsPixelInfoEnabled` 默认为 true，控制左下像素显示和查询订阅，右键菜单共享该设置。两者都可在显示前配置。`EndInteraction()` 取消未完成创建或结束编辑，保留已完成测量和已应用修改。

`FitToViewport` 将完整图像居中，可能选择超出滚轮常规 0.01–100 范围的缩放。此时滚轮允许朝该范围变化，拒绝继续远离该范围。

用 `SetDisplayRange(range)` 接受灰度映射请求，不等待显示；`DisplayRange` 读取请求，快照保留最后成功提交的映射。后续显示失败通过 `DisplayFailed` 观察。完整规则见[显示契约](frame-pipeline.md#提交与显示)。

### 排空与独立租约

关闭排空已接纳提交，直到释放和公开结果完成，包括提交线程上的替换清理以及冻结/取消丢弃。不响应取消的像素源可使关闭等待其工作释放租约。程序释放不受 `CanUserClose` 或取消窗口关闭事件的处理器阻止。所属查询租约和菜单固定帧的清理错误保留至关闭结果；普通查询/提供者失败仍归查询结果，不自动变成关闭失败。

关闭开始后，帧获取、快照捕获、注册和图层操作抛出 `ObjectDisposedException`；提交仍接管所有权并返回 `Closed`。外部独立租约和快照继续由持有者拥有；Viewer 不等待这些租约，也不提前回收其存储。已接纳且自持依赖的捕获或导出可在关闭后完成。

## 不拥有关闭责任时观察停止

`IViewerLifetime.WhenStopping` 是两种宿主共同实现、由 `IViewerCapabilities` 继承的只读共享任务。
端点可以同时借用它和实际需要的窄能力接口。读取或等待该任务不会发起关闭。
共享接纳门关闭时任务成功完成，包括窗口关闭、显式释放和宿主 Dispatcher 提前结束。
晚到的观察者仍能看到完成状态，后续清理失败不会使它失败或取消；临时 `Unloaded` 不触发该信号。

端点据此停止转发新操作，但并发提交仍转移帧所有权并可能返回 `Closed`，其他操作可能抛出
`ObjectDisposedException`。端点必须排空自身已接纳的工作，才能释放其依赖。
信号完成不证明资源已排空、已释放或独立 STA 已退出，独立租约也可以越过查看器生命周期。
拥有者仍需等待 `DisposeAsync` 观察共享关闭结果，失败结果不证明排空完成。
信号不会内联调用 continuation，也不承诺在 UI 线程交付；等待已完成任务仍可能同步继续，
不要在 UI 线程同步阻塞等待。

## 借用能力接口

`IViewerCapabilities` 汇总以下接口及全局 `Layers`，`IViewer` 额外继承 `IAsyncDisposable`。适配器需要完整查看能力时借用聚合接口，否则接受所需窄接口。两种宿主在同一实例上实现这些接口，不增加包装或独立生命周期，共享 STA、状态和关闭接纳边界；仅拥有者释放 Viewer。

| 接口 | 命名空间 | 职责 |
| --- | --- | --- |
| `IFrameSink` | `.Frames` | 转移帧并等待接收结果 |
| `ICommittedFrameSource` | `.Frames` | 获取当前原始帧租约，观察提交 |
| `IViewerLifetime` | 根命名空间 | 被动观察最终停止接纳，不发起关闭 |
| `IViewerDisplay` | `.Viewport` | 适应窗口与显示范围 |
| `ISnapshotSource` | `.Snapshots` | 捕获独立拥有的整图/区域快照 |
| `IViewerDrawing` | `.Drawing` | 默认 `Markers` 层和绘图辅助方法 |
| `IViewerMeasurements` | `.Measurements` | 测量层、工具、交互、样式和事件 |
| `IViewerHud` | `.Hud` | 屏幕文字及像素显示 |
| `IViewerMenu` | `.Menus` | 可撤销菜单注册 |
| `IViewerQueries` | `.Imaging` | 测量和 HUD 共用查询设置及指标 |

```csharp
using Fizzy.ImageViewer.Frames;

static ValueTask<FrameSubmitResult> PublishAsync(IFrameSink sink, ImageFrame frame,
    CancellationToken cancellationToken = default) => sink.SubmitFrameAsync(frame, cancellationToken: cancellationToken);
```

帧接收器即使拒绝或取消也接管帧；替代实现和测试替身必须在每个终止路径释放它。读取者拥有获得的租约。提交事件与后续获取不是原子操作；需要特定提交的帧时，使用 `FrameSubmissionOptions.OnCommitted` 中仅在回调期间借用的租约。

只有 `IViewerCapabilities.Layers` 暴露跨图层创建、枚举、移除和清空。`Layers.Items` 是成员快照，元素仍为可变句柄。`IViewerDrawing.Markers` 和 `IViewerMeasurements.Measurements` 分别暴露整个对应图层；清一层不清另一层。组件只更新一项时，传入 `DrawingHandle`；需要管理一层时，传入 `DrawingLayer`。图层能力包括该层全部内容和点击句柄，不限于当前调用方创建的项。

测量事件带有不可变快照和可编辑/删除的句柄；工具注册与 `EndInteraction` 管理 Viewer 全局状态，工具会话应使用自己的 `context.Finish()`。查询设置影响测量和 HUD 的共享调度器，也影响其他调用方；菜单和 HUD 句柄只管理自己的注册或文字。

这些接口表达类型依赖，不提供安全隔离。实现和事件 sender 都是同一个 Viewer，可通过转换恢复完整门面；绘图也仍依赖 WPF。拥有者始终需要等待 `IViewer.DisposeAsync()`。

## 事件订阅

库自定义事件使用 `EventHandler` 或 `EventHandler<T>`，直接复用现有载荷对象或值，
`T` 不要求继承 `EventArgs`。sender 为事件所属的公开对象：Viewer、ImageViewerControl、
DrawingLayer 或 IMeasurement。回调运行在所属 UI/STA 线程；`IViewerWindow.Closed` 仍仅属于
独立窗口。`WhenStopping` 是任务，不承诺在 UI 线程交付。

查看器停止接纳，或所属图层、测量移除后，添加订阅抛出 `ObjectDisposedException`。
移除订阅在终止后仍安全、幂等。重复订阅会重复调用，每次退订移除最后一个匹配项。
每次通知捕获订阅者：交付期间新增订阅只参与后续通知，退订不撤回已经捕获或排队的回调。
最终清理清空订阅。需要观察窗口关闭时在显示前订阅，`Closed` 不补发；晚到的终止观察使用 `WhenStopping`。

订阅者同步异常逐个记录并隔离，不阻断后续订阅者，也不使原操作失败。
不会等待 async-void 处理器；订阅者自行管理异步工作及错误。`IMeasurement.OnDispose` 等清理回调
保留独立的失败报告契约。事件载荷描述本次通知，实时句柄可能已变化或释放。
测量通知保留 FIFO 和重入排序；帧及绘图点击事件不使用该队列。

## 相关能力

- [测量](measurements.md)：交互、不可变结果及通知顺序。
- [自定义工具](measurement-tools.md)：注册与一次性创建会话。
- [菜单](menus.md)：注册、目标捕获和异步执行。
- [绘制与 HUD](drawing-layers.md)：图层及单项句柄。

[英文 API 参考](../../artifacts/docs/api/index.md)列出公共符号。内部 WPF 控件和视觉树不是扩展点；实现边界和 API 基线见[架构](architecture.md)。
