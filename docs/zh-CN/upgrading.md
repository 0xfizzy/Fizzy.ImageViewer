# 升级现有应用

[English](../en/upgrading.md)

## 从 1.2.0 升级到 1.3.0

**1.3.0 包含破坏性 API 和行为变化。** 请修改并重新编译消费者；针对 1.2.0 编译的二进制文件不保证能直接使用新程序集。
已有应用按以下步骤迁移，新应用可从[入门](getting-started.md)或[嵌入指南](embedding.md)开始。

### 1. 更新包引用

1.3.0 发布后，在项目或中央包版本文件中将核心包 `Fizzy.ImageViewer` 更新为 1.3.0，并还原依赖。
包包含 `net8.0-windows` 和 `net10.0-windows`，保持启用 `UseWPF`。升级库不要求将 .NET 8 应用迁移到 .NET 10。

若使用可选 CUDA 扩展，先按第 5 步更新其引用，再还原依赖。核心包与 CUDA 包独立发布；核心版本发布成功不代表配套 CUDA 包已经可用。

### 2. 修改接口、事件与显示设置

`IViewer` 不再包含 `IViewerWindow`。将 `Show`、`Hide`、`Minimize`、窗口边界、标题和 `Closed` 保留在 `Viewer` / `IViewerWindow` 上。
借用共享功能时使用 `IViewerCapabilities`；所有者需要 `DisposeAsync` 时保留 `IViewer`。
只有独立窗口 `Viewer` 实现窗口接口。

`FrameCommitted`、`IMeasurement.GeometryChanged` 和 `IMeasurement.QueryResultChanged` 改用 `EventHandler<T>`：

```csharp
// 1.2.0
viewer.FrameCommitted += frame => Console.WriteLine(frame.FrameId);
// 1.3.0
viewer.FrameCommitted += (sender, frame) => Console.WriteLine(frame.FrameId);
```

sender 为公共事件所有者：`Viewer`、`ImageViewerControl` 或测量对象。
终止后添加库事件处理器会抛出异常，移除处理器仍然安全。请在提交帧前订阅事件，详见[事件契约](public-api.md)。

显示范围更新只接收配置，不等待显示：

```csharp
// 1.2.0
await viewer.SetDisplayRangeAsync(range);
// 1.3.0
viewer.SetDisplayRange(range);
```

通过 `DisplayFailed` 观察后续渲染错误。快照使用最后提交的映射，可能不同于最新请求的范围。

### 3. 区分接收与显示

`SubmitFrameAsync` 现在只等待接收完成。将使用 `Committed`、`Superseded` 或 `Failed` 的分支改为处理 `Accepted`、`Cancelled`、`Frozen` 和 `Closed`。
先检查 `result.Error` 中属于接收操作的清理失败，再处理状态：

```csharp
// 1.2.0：完成结果可表示帧已提交显示。
var result = await viewer.SubmitFrameAsync(frame);
if (result.Status == FrameSubmitStatus.Committed)
    Console.WriteLine("Committed");
```

```csharp
// 1.3.0：完成结果只表示接收，不表示显示。
var result = await viewer.SubmitFrameAsync(frame);
if (result.Error is { } error) throw error;
if (result.Status == FrameSubmitStatus.Accepted)
    Console.WriteLine("Accepted; display may follow");
// 按应用策略处理 Cancelled、Frozen 和 Closed。
```

枚举数值已改变，不要按新含义解释持久化的旧数值状态。
**Accepted 不代表已经显示，也绝不代表已转移的存储可以复用。** 已接收帧可能被替换而没有显示通知；取消仅在接收前检查。
隐藏、卸载或最小化的宿主保留最新等待帧，恢复后无需再次提交即可显示。

通过 `FrameCommitted` 或提交选项的 `OnCommitted` 回调观察 UI 提交，通过 `DisplayFailed` 观察渲染失败。
不要无条件等待每个已接收帧都提交显示。UI 提交不证明图像已经呈现在物理显示器上。

### 4. 保持所有权并等待关闭

提交立即转移帧所有权，包括拒绝和取消。仅在最终释放回调中回收已转移的缓冲区；借用或需要复用生产者缓冲区时，使用 `ImageFrame.Copy`。
已接收存储的后续清理失败通过 `DisposeAsync` 报告，不进入较新的接收结果。

`OnCommitted` 仅在回调返回前借用租约。后续工作须取得独立租约并在完成后释放。
外部独立租约可以越过 Viewer 生命周期；Viewer 不等待它们，也不提前回收其存储。

`WhenStopping` 表示接纳已关闭，不表示在途工作已排空。所有者仍须等待 `DisposeAsync`，观察清理完成或共享的失败结果。
不要在 UI 线程同步阻塞等待释放。

嵌入时在宿主 STA 创建 `ImageViewerControl`，并在其 Dispatcher 停止前等待释放完成。
`Unloaded` 只结束交互，保留内容以便在同一 Dispatcher 重新挂载，不等于释放。
最终释放不会关闭宿主窗口或 Dispatcher；最终释放后须新建实例。完整示例见[嵌入与关闭](embedding.md)和[帧所有权](frame-pipeline.md)。

### 5. 迁移可选 CUDA 包

移除旧 `Fizzy.ImageViewer.Cuda` 包引用，根据宿主 CUDA 主版本，从 `Fizzy.ImageViewer.Cuda11`、`Fizzy.ImageViewer.Cuda12`、`Fizzy.ImageViewer.Cuda13` 中选择一个。
对应变体发布后，使用其 1.3.0 版本配套核心 1.3.0。同步更新中央版本以及直接、传递依赖的消费者；不同变体不能混装，也不能与旧包共存。

所有变体保持 `Fizzy.ImageViewer.Cuda` 命名空间和程序集名不变，无须修改命名空间。
每个都是包含 Windows x64 原生组件的完整包。工具链、驱动和架构要求见 [CUDA 构建与部署](cuda.md)。
核心发布成功本身不证明 CUDA 包可用，也不证明 GPU 执行覆盖。

### 6. 重新编译并验证受影响集成

使用新引用重新编译所有消费者与适配器，按上述步骤检查状态处理、事件回调、缓冲区回收和关闭逻辑。
运行应用现有测试，覆盖实际使用的集成路径；适用时包含隐藏后恢复显示和嵌入宿主关闭。
仅编译通过无法验证提交完成含义的变化。仓库命令与验证边界见[验证指南](validation.md)。
