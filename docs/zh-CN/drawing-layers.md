# 绘制与 HUD

[English](../en/drawing-layers.md)

在图像坐标中绘制标记，在屏幕坐标中显示状态文字。以下示例假定已有存活的 `viewer`；请保留返回的句柄，直到该功能结束。

## 绘制标记

```csharp
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

using var marker = viewer.Layers.Markers.Add(
    new CircleElement(new Point(100, 100), 8, Brushes.Red));
marker.Replace(new CircleElement(new Point(120, 100), 8, Brushes.Lime));
```

`Viewer.Layers` 管理图像坐标中的业务图层。用 `CreateDrawingLayer` 创建自定义层，`ClearContents` 清空内容并保留图层，`RemoveLayer` 清空并移除自定义层。

默认标记层 `Markers` 的 `ZIndex` 为 0，关闭命中测试；测量层 `Measurements` 为 1000，开启命中测试；自定义层为 100，关闭命中测试。较高的 `ZIndex` 显示在上方，相同时按创建顺序排列。图像位于业务图层下方，HUD 位于其上方。

`Markers` 和自定义层都是 `DrawingLayer`，提供 `Add` 和 `DrawingClicked`。`Measurements` 由测量工具管理模型内容，不提供批量绘图。`ViewerLayers` 和 `ViewerLayer` 位于 `Fizzy.ImageViewer.Layers`；图层共享名称、可见性、命中测试、`ZIndex` 和 `Clear`，`Items` 返回这些公共图层句柄的成员快照。

## 绘制句柄

`DrawingHandle` 管理一项绘图，可以包含零个或多个元素。`Add` 的单元素和集合重载都只创建一个视觉对象；`Replace` 替换完整内容，不追加元素。替换可在不同形状、单项、集合和空集合之间切换，同时保留句柄身份和叠放顺序。

空集合保留句柄但不绘制，也不参与命中。`DrawingClickedEventArgs.Drawing` 标识整项绘图，不提供单个元素索引；释放句柄会移除整项。

`DrawLine`、`DrawCircle`、`DrawRectangle`、`DrawCrosshair` 和 `DrawText` 都返回相同的 `DrawingHandle`，方法名只限定初始内容。每次调用创建独立视觉对象；大量标记应按独立管理组一次提交集合。需要指定图层时，使用该层的 `Add`。

## 批量标记与测量共存

批量标记用于减少视觉对象数量；交互测量则保留独立 WPF 元素及模型。文本通过 `FormattedText` 绘制，不为每个标签创建布局控件。

```csharp
using Fizzy.ImageViewer.Measurements;
using Fizzy.ImageViewer.Drawing;
using System.Windows;
using System.Windows.Media;

var markers = viewer.Layers.CreateDrawingLayer("detections");
using var batch = markers.Add(Enumerable.Range(0, 10000).Select(i =>
    new CircleElement(new Point(i % 100 * 10, i / 100 * 10), 3,
        Brushes.Red, 1, Brushes.Red) { ScaleMode = OverlayScaleMode.FixedSize }));

viewer.ActivateMeasurementTool(MeasurementToolIds.Length); // Also: Point, RectangleRoi, LineProfile IDs.
// Measurement results and their editing controls belong to Measurements.
// Marker visuals do not intercept input by default.

batch.Replace(new DrawingElement[] {
    new CircleElement(new Point(100, 100), 8, Brushes.Lime, 2),
    new LineElement(new Point(10, 10), new Point(90, 90), Brushes.Cyan, 3),
    new RectangleElement(new Rect(20, 20, 50, 40), Brushes.Yellow),
    new CrosshairElement(new Point(200, 100), Brushes.Red, 12),
    new TextElement(new Point(100, 100), "target", Brushes.White, 18, new Vector(8, -20))
});
markers.IsVisible = true;
markers.ZIndex = 200;
markers.IsHitTestVisible = true;
markers.DrawingClicked += (_, e) => {
    // e.Drawing identifies the entire drawing; no per-element hit index or editing.
    // e.ImagePosition is in image pixels; e.Button identifies the mouse button.
};
// markers.Clear();                 // Invalidates every batch in this layer.
// viewer.Layers.RemoveLayer(markers); // Clears and removes a custom layer.
```

完整持续更新示例见 [LiveOverlays](../../samples/LiveOverlays/Program.cs)。

## 坐标、尺寸与输入

绘图和测量位置使用连续图像坐标：`(0,0)` 为图像左上边界，`(0.5,0.5)` 为首像素中心。
位置保留小数，绘图不取整，也不自动添加中心偏移。`PixelCoordinate` 表示整数像素索引，
标记该像素中心时使用 `ToImageCenter()`。若算法以整数坐标表示像素中心，消费者的显示适配器
应对连续结果统一加 `(0.5,0.5)`，仅转换一次；不得取整亚像素结果，也不得偏移图像边界、
鼠标位置或已经采用 Viewer 坐标的位置。

OverlayScaleMode 与定位独立，不支持模式在内容变更前拒绝。屏幕尺寸为 WPF DIP，不是物理像素。

- 线、矩形、圆默认 FixedStroke：几何为图像像素，线宽为 DIP；ScaleWithImage 同时缩放。
- 圆额外支持 FixedSize，中心为图像坐标，半径/线宽为 DIP。
- 准星默认 FixedSize，也支持其他两种；ArmLength 为中心至端点距离，中心环半径为其一半。
- 文本默认 FixedSize，字号/偏移为 DIP，ScaleWithImage 随图像缩放；默认 Segoe UI。
- 平移只改共享变换，依赖缩放的批次每渲染周期重绘一次，DPI 变化刷新文字。
- 滚轮、中键平移和像素查询使用公共视口输入面，包括可命中叠加上方。
- 命中实际填充、轮廓、字形，不是批次边界；空心圆内部和空白透传，重叠时最高启用层最后添加者优先。
- DrawingClicked 在 STA 消耗鼠标按下，回调保持短小；订阅错误逐个记录隔离，后续订阅仍接收，事件仍被消费。
- 测量暂时抑制业务层命中，不改配置标志；结束恢复当前配置，包括期间变更和新增层。

## 所有权与生命周期

提交会复制集合，后续修改原集合不会更新绘图。带冻结画刷的不可变元素可以共享；可变 WPF 画刷必须在其所属调用线程复制并冻结。不可冻结的资源、null 元素、非有限坐标、非正半径/线宽/字号，以及不支持的缩放模式都会在内容改变前被拒绝。

`Replace` 完成验证和绘制命令录制后才发布内容；录制失败保留原有绘图。所有视觉操作调度到 Viewer STA。`Dispose` 同步移除且幂等；清空、移除图层及 Viewer 关闭会使句柄失效，之后调用 `Replace` 抛出 `ObjectDisposedException`。

内置层不可移除；图层名称非空且按大小写区分唯一，图层句柄不能交给另一 Viewer 的管理器。`Items` 是成员快照，其中句柄仍可变。`Clear` 保留设置和订阅，移除及关闭则释放资源和订阅。新增点击订阅会调度到 STA，并拒绝已移除的层或正在关闭的 Viewer；退订在关闭后仍安全。

IViewer.Layers 允许全局管理；IViewerDrawing 只暴露默认 Markers，测量能力暴露整层。整层句柄能清除他人内容，组件只需更新一项时传 DrawingHandle；需要创建或全局清理后重建时才保留层。

可编辑测量通过工具上下文创建、更新几何并 Complete，拥有框架视觉、标签、查询及注册资源。不支持附加任意 WPF 元素，内部 shape factory/MeasurementOverlay 不是公共接口。

ClearContents/Clear All Shapes 取消测量并清全部业务层，保留 HUD。先取得层快照，回调新建层存续，已被移除层跳过，其余按顺序清理。清测量层也取消创建/编辑。显式清理尝试所有安全步骤并传播聚合错误，菜单/输入记录错误，关闭错误进入 DisposeAsync。快照只导出图像，不导出叠加。

## HUD 文字

```csharp
using var status = viewer.DrawHudText("Ready", System.Windows.Media.Brushes.White);
status.Update("Running", System.Windows.Media.Brushes.Lime);
```

`HudLabelText` 控制右上标签，设为 null 或空字符串会隐藏标签，再次读取返回空字符串。`IsPixelInfoEnabled` 独立控制像素 HUD，可在显示前设置。`HudTextHandle` 和 `AnchorAlignment` 位于 `Fizzy.ImageViewer.Hud`。

`DrawHudText` 返回可更新文字和颜色的句柄。位置、对齐方式及 `double` 字号在句柄生命周期内固定，改变布局需要重新创建。创建和更新在调用线程复制画刷，再调度到 Viewer STA；可变画刷必须从所属线程访问。

null 文字或画刷、不可冻结画刷、非法锚点/对齐方式，以及非正或非有限字号都会被拒绝。释放幂等；Viewer 关闭使句柄失效，之后 `Update` 抛出 `ObjectDisposedException`。清空业务层不清除 HUD。

## 验证

```powershell
dotnet test tests/Fizzy.ImageViewer.Tests
dotnet run -c Release --project tools/DrawingBenchmark
```

基准覆盖 1,000/10,000 圆的创建、替换、含布局的缩放重绘，与逐 Shape 路径比较；时间依机器且不含 GPU 呈现，Visual 数与功能行为是自动验收依据。
