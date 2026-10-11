# 实时流集成

[English](../en/live-streaming.md)

完成[入门](getting-started.md)后，可按本指南集成相机或其他连续图像源。Viewer 限制自身的帧队列；应用负责采集、上游缓冲、叠加更新调度和生产者关闭。

先阅读 [LiveOverlays](../../samples/LiveOverlays/Program.cs)：该示例使用模拟图像源，复用绘图句柄，并在释放 Viewer 前停止生产。

## 在提交前限制工作量

Viewer 同时处理一个提交，只保留最新的一个等待帧；被替换的等待帧返回 `Superseded`。这适合实时显示，不是无损录像队列。

拉取式生产者可像 [LiveOverlays](../../samples/LiveOverlays/Program.cs) 一样，等待每次提交后再生成下一帧，自然限制生产工作量。推送式相机回调则应在应用侧设置有界的“只保留最新帧”交接，由工作线程提交并观察每个结果；提交前丢弃的自有帧也必须释放。避免不断增长的 `Task.Run` 队列、无界通道或提交任务列表：Viewer 内部的有界队列不能限制上游已经分配的工作。关闭时停止接纳并排空生产者。

等待提交只能观察提交或丢弃结果，以及该提交的释放错误，不能证明相机缓冲区可复用。对于已转移的存储，只有最终释放回调能确定这一点。统计各状态数量和错误，避免在热路径回调中逐帧记录成功日志。

## 实时相机叠加的消费者要求

层和批次应持续整个显示会话。初始化时创建句柄，在更新循环调用 `Replace`；关闭时先停止生产并等待在途更新，再清理句柄。

| 避免 | 使用 |
| --- | --- |
| 每帧 Clear/Add 或释放重建 | 保留句柄并 Replace |
| 每检测目标调用一个 Draw | 每独立管理组提交整个集合 |
| 每结果投递 Dispatcher 或启动任务 | 一个最新等待结果及单个限频消费循环 |
| 每帧重绘静态 ROI/准星/标签 | 单独保留静态批次，仅变化时更新 |
| 每帧创建可变 Brush | 复用 Brushes.Lime 等冻结 Brush |
| 每状态重建 HUD | 保留句柄，内容变化时 Update |

以下操作分别属于初始化、更新和关闭阶段，不应全部放入每帧回调：

```csharp
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

// Initialization: retain these handles for the display session.
var detections = viewer.Layers.CreateDrawingLayer("camera-detections");
var batch = detections.Add(Array.Empty<DrawingElement>());

// Display update: call from ONE serialized consumer at the chosen display rate.
// Build this collection only for the latest result selected for display.
batch.Replace(new DrawingElement[] {
    new CircleElement(new Point(100, 100), 8, Brushes.Lime, 2),
    new RectangleElement(new Rect(20, 20, 50, 40), Brushes.Yellow)
});

// A valid result with no detections clears content while retaining the visual.
batch.Replace(Array.Empty<DrawingElement>());

// Shutdown: first stop and await the producer/display loop, then release handles.
batch.Dispose();
viewer.Layers.RemoveLayer(detections);
```

显示频率与采集/推理独立，可按实测成本选择 30/60 Hz。旧等待结果被覆盖而不积队列；没有新结果不 Replace，新空结果 Replace 空集合。选定最新结果后才创建绘图对象，释放被覆盖结果的池化资源，不在消费者读取时修改结果。

Replace 同步调度到 STA，不限频、不合并、不丢弃；帧管线的最新帧队列不适用于叠加。提交图像和替换叠加并非原子同帧呈现，消费者自行跟踪身份和过期策略。调用 Viewer 时不持生产者锁。

复用 Visual 不等于无分配：集合会复制，冻结 Brush 的不可变元素可共享，可变 Brush 需克隆冻结；WPF 命令、资源和文本仍分配。一次准备内用有界缓存共享相同画笔，不承诺零 GC 或帧率，也不要每帧 GC.Collect。

Clear、移除层和关闭使句柄失效。如果实时运行支持 Clear All Shapes，应与更新循环串行，恢复前重建一次句柄，不逐帧重试失效对象。停止订阅/计时器并等待在途更新再销毁。

按预期标记数量和缩放行为测量分配、GC 次数与更新延迟；基准范围见[验证](validation.md#绘制基准范围)。

## 减少复制与转换

- 保留源数据已支持的格式和步长，不要仅为显示而将 Gray8 扩展成 BGRA，或交换 RGB/BGR 通道。支持的原生 8 位 CPU 布局会保留；Gray16、Gray32Float、Rgba32 和调整显示范围的 Gray8 需要显示转换。
- 使用 `Copy` 时可复用生产者缓冲区；使用 `TakeOwnership` 时应转移独立拥有、不可变的缓冲区。`Copy` 内部已租用内存，生产者侧额外池化应先测量分配和保留情况。绝不能提前覆写已转移的内存。
- 在一次窗口生命周期内复用 Viewer。尺寸和显示格式稳定时，可复用内部两张 CPU WriteableBitmap。只在需要时修改显示范围，不要逐帧请求重绘。
- 原始像素已在 GPU 上时，用支持的 GPU 适配器避免整图往返传输。对于 CPU 输入，GPU 适配并不自动更快，仍需考虑分配、同步与复制成本。

## 限制查询开销

像素查询、线剖面和区域统计不必跟随相机帧率。可在显示窗口前配置较低频率：

```csharp
viewer.QueryOptions = new Fizzy.ImageViewer.Imaging.PixelQueryOptions
{
    PixelQueryRateHz = 15,
    LineQueryRateHz = 10,
    RegionQueryRateHz = 5,
    MaxResultAge = TimeSpan.FromMilliseconds(200)
};
```

这些数值是应用取舍，不是性能保证。Viewer 同时只执行一个共享查询批次，像素 HUD 还额外限制为最高 10 Hz。`MaxResultAge` 控制是否接受过期结果，不会加快查询。使用 `QueryMetrics` 观察耗时与过期结果数量。优先请求点或 ROI，避免整图快照，GPU 源尤其如此。及时释放自有租约、区域和快照，它们会占用内存并可能延长相机缓冲区寿命。直接调用 CPU 查询方法可能在调用线程同步执行，昂贵分析应移出 UI 线程。

## 保持回调短小，测量完整链路

`OnCommitted` 和 `FrameCommitted` 在 Viewer STA 执行。不要在其中编码、等待设备、执行重分析、同步等待任务，或再次提交并等待结果。需要异步处理时，在回调返回前获取独立租约，工作完成后释放；不能直接保留回调借用的租约。此类后台工作同样需要有界。

分别测量生产者的转换与复制成本、托管分配、提交状态数量、查询耗时和叠加更新成本。提交完成不等于物理显示延迟。对调整的路径运行[帧、绘图和查询基准](validation.md#线剖面分配)，再在目标机器验证交互和显示。合成基准不能证明持续相机吞吐量或混合 DPI 表现。
