# 测量与交互

[English](../en/measurements.md)

使用内置工具测量点、长度、矩形区域和线剖面。位置与长度采用图像像素单位；不需要交互编辑的标记使用[绘制层](drawing-layers.md)。

## 激活内置工具

以下片段假定已有[入门示例](getting-started.md)中的存活 `viewer`。

```csharp
using Fizzy.ImageViewer.Measurements;

viewer.ActivateMeasurementTool(MeasurementToolIds.Length);
```

| 工具 ID | 操作 | 结果 |
| --- | --- | --- |
| `Point` | 点击一个点 | 像素值 |
| `Length` | 点击两个端点 | 图像像素单位的长度 |
| `RectangleRoi` | 点击两个对角点 | 区域统计 |
| `LineProfile` | 点击两个端点 | 像素样本和曲线窗口 |

也可从右键菜单选择工具。未知 ID 抛出 `KeyNotFoundException`；测量层隐藏或禁用命中测试时，激活抛出 `InvalidOperationException`；关闭开始后抛出 `ObjectDisposedException`。

## 读取结果与通知

订阅 `Viewer.MeasurementChanged` 即可观察所有工具产生的已完成测量，无需访问 WPF 视觉树。几何编辑、查询发布及结果失效都会携带不可变几何、版本和当前 `QueryResult`。几何变化、查询失败或过期后，结果为 `null`。预览不触发该事件；相等的几何更新和已经为空的结果也不重复通知。

```csharp
viewer.MeasurementChanged += (_, e) =>
{
    var id = e.Snapshot.Id;
    var geometry = e.Snapshot.Geometry;
    var result = e.QueryResult; // Null means the measurement currently has no valid pixel result.
    // Retain these immutable values or dispatch them to the application's UI.
};
viewer.ActivateMeasurementTool(MeasurementToolIds.RectangleRoi);
```

`MeasurementCompleted` 每项只触发一次，且不代表像素查询已经就绪；`MeasurementRemoved` 只对曾完成的测量触发。事件快照包含 `Id`、完整 `Geometry`、`GeometryVersion` 和查询结果。可保留不可变载荷，或将其转交应用 UI 线程。

Viewer 和句柄的通知共用 STA 上的 FIFO 队列。每次变化先捕获载荷和订阅者；订阅者重入产生的新事件排在当前通知之后。几何版本不会倒退，完成先于删除，删除后不再变更。收到旧快照时，事件中的实时 `Measurement` 句柄可能已经变化或释放。

订阅者中关闭窗口仍会交付已排队的测量通知，`Closed` 晚于删除通知。订阅异常逐个记录并隔离。事件中的句柄可从任意线程更新或释放，也可在通知中重入释放。自定义工具还可订阅句柄的 `GeometryChanged` 和 `QueryResultChanged`。

## 编辑与删除

点击已完成测量进入编辑，拖动控制点改变几何；Delete 删除选中项，Escape 结束创建或编辑并保留已应用的修改。程序可通过事件的 `Measurement` 句柄调用 `UpdateGeometry` 或 `Dispose`；`viewer.Measurements.Clear()` 清空全部测量。

## 几何与更新

`MeasurementGeometry.Point`、`Crosshair`、`Line`、`Rectangle` 和 `Circle` 创建封闭的不可变几何记录。通过具体类型模式匹配读取属性：点和准星使用 `Position`，线使用保留方向的 `Start`/`End`，矩形标准化两个对角，圆使用 `Center`/`Radius`。`Bounds` 分别为零范围、端点包围框或圆的直径框。坐标和范围必须有限，圆半径允许为零。

每次真实几何变化增加 `GeometryVersion` 并清除查询结果；相等更新不产生变化，已有测量不能改换几何种类。框架在通知前更新模型、标签、主图形和编辑控制点。标签的文字、内边距和偏移保持屏幕尺寸，锚点跟随图像；查询缓慢或失败不会延迟几何标签。

控制点重合时，优先命中用于调整范围的控制点，因此零半径圆、零长度线和塌缩矩形仍可展开。矩形拖动使用开始时的对角锚点，越过该点不会改变固定角；拖动期间的程序更新替换基线，后续拖动继续使用相同控制点索引。直接修改 WPF 属性不属于测量 API。

## 查询与保留结果

自定义工具通过 `MeasurementOptions.Query` 选择查询：

| 查询 | 支持几何 | 结果 |
| --- | --- | --- |
| `None`（默认） | 全部 | 几何标签，不订阅像素 |
| `Pixel` | 点、准星 | 样本及整数源坐标 |
| `LineProfile` | 线 | 有序样本及源坐标 |
| `RegionStatistics` | 矩形 | 裁剪区域及通道统计 |

非法组合在附加视觉前拒绝。点坐标向下取整；线查询裁剪至图像并保留方向和两个端点。点和线使用半开覆盖 `[0, Width) × [0, Height)`，因此最后一行/列的小数位置仍可采样，但仅位于右/下边界的线没有样本。ROI 查询和区域导出共用 `PixelRegion.Clip`；空目标没有结果。

`ShowProfileWindow = true` 请求在完成时创建曲线窗口，只能与线剖面查询组合；内置线剖面工具开启它，自定义查询默认只返回数据。关闭曲线窗口会删除测量。`LineMeasurementGeometry.Length` 不读取帧，纯长度测量没有查询结果；计算避免中间平方溢出/下溢，仅当最终长度超出 `double` 表示范围时返回正无穷。

`QueryResult` 在成功发布前为 `null`。结果包含测量 ID、几何版本、来源 `FrameInfo`、查询种类，以及只读样本、坐标或统计副本；不借用调度器缓冲区，可跨帧、编辑和删除保留。

模式匹配 `MeasurementSampleResult` 可读取 `Coordinates`/`Samples`：`Pixel` 为一对，`LineProfile` 为有序多对。`MeasurementRegionStatisticsResult` 提供非空裁剪 `Region` 和 `Channels`。公共基类只提供来源与查询种类，`None` 没有结果。标签和曲线先于通知更新；失败、描述符变化、目标消失和过期会清除旧结果。

## 所有权与样式

`IViewerMeasurements.Measurements` 暴露整层设置和清理，影响其中所有测量；清空会取消交互并删除测量，但保留绘图和 HUD。工具注册和 `EndInteraction` 也作用于 Viewer 全局状态；工具内部应使用上下文的 `Finish` 结束自身激活。事件携带可编辑、可删除的实时句柄，并非只读能力。

`Viewer.MeasurementStyle` 控制未来创建的测量。赋值在调用线程复制并冻结所有画刷；已有项保留其普通和选中样式。创建时的 `MeasurementOptions.Style` 可覆盖默认值，也会生成快照。

每项测量拥有几何、框架视觉、标签、可选查询和注册资源。预览已经是被拥有的测量；`Complete` 保留它并启用查询，取消会释放未完成项。完成时不重新附加视觉。菜单同时固定帧和 ROI，后续编辑不改变已捕获导出目标。

删除、清空、曲线关闭和 Viewer 关闭都进入幂等释放。专用资源清理失败后仍尝试移除标签和主图形；移除后才发出通知。Viewer 关闭先停止交互和查询、释放测量，再清图层和帧；`DisposeAsync` 等待在途查询释放自己的租约，包括不立即响应取消的后端。

## 交互规则

有效的测量启动结束编辑，有效的编辑启动取消创建；无效编辑请求保留当前会话。隐藏测量层、禁用其配置的命中测试、清空或关闭 Viewer 会取消活动会话并释放鼠标捕获。隐藏测量层保留完成项，但取消预览；`Viewer.Hide()` 保留活动工具和预览。丢失鼠标捕获结束当前拖动但不删除项；捕获失败保留编辑状态，不开始拖动。

测量期间的临时输入抑制不修改图层的配置标志。恢复输入时重新读取每层当前配置，包括期间新增的层。批量清理期间拒绝创建测量和启动新会话；重入清理是幂等的，一层失败后仍尝试其他层。

## 查询发布

`QueryOptions` 控制共享查询频率，`QueryMetrics` 提供测量数据；执行规则见[帧与像素](frame-pipeline.md#像素访问与测量)。发布要求注册实例、测量身份、几何版本和帧描述符仍有效；重新订阅不会接纳旧工作的结果。查询和发布失败按订阅隔离。

同描述符的新帧不会自动否定在途结果，结果可在 `MaxResultAge` 范围内发布，避免高帧率下持续饥饿。描述符变化和超龄结果被拒绝；未变化冻结帧的有效结果无需反复查询。像素 HUD 单独使用移动结果策略：同会话内发布完整坐标/值，完成后限频至 10 Hz，变化时旧显示最多保留 300 ms；不放宽测量的几何检查。

## 线程与释放

句柄的读取、更新和订阅调度到 Viewer STA。回调保持短小，不要同步等待会调用 Viewer 的工作线程。关闭后 `Dispose` 和退订仍安全，`Id`、`Origin`、`IsDisposed` 仍可读；其他访问要求 Viewer 存活。显式释放和清空在必要安全清理后传播错误，关闭清理失败进入共享 `DisposeAsync`；通知异常记录并隔离。

## 自定义工具

注册、会话创建、资源转移、重入和失败处理见[自定义测量工具](measurement-tools.md)。

曲线窗口以样本索引为横轴、像素值为纵轴，不改变原始样本；关闭窗口会删除测量。渲染实现见[架构](architecture.md#线剖面渲染)。
