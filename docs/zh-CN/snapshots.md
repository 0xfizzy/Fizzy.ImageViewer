# 快照

[English](../snapshots.md)

`CaptureSnapshotAsync` 捕获已提交图像，而不是最新提交请求。整帧和区域重载返回独立拥有像素的 `ImageSnapshot`，使用后必须释放。

## 捕获与保存

`Raw` 保留原始值，必须使用 TIFF。`Display` 应用捕获时已提交的映射，支持 TIFF、PNG、JPEG、BMP；JPEG 将透明部分合成到黑色背景。绘图、测量标签和 HUD 不属于图像像素。

```csharp
using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
await snapshot.SaveAsync(path, SnapshotEncoding.Png);
```

参见[完整导出示例](../../samples/MeasurementsAndExport/Program.cs)。

区域必须非空且完全在帧内。没有输入或容量已满时抛出 `InvalidOperationException`。每个 Viewer 最多接纳两个捕获，一次执行一个导出读取；容量拒绝发生在获取租约之前。接纳时同步固定目标，排队不会改用较新的帧。

## 所有权、取消与失败

已接纳捕获拥有关闭后完成所需的依赖，结果由调用方拥有。`AcquirePixels()` 返回独立租约，可越过快照和 Viewer 生命周期。像素使用区域局部坐标，`Region` 和 `SourceFrame` 保留原始来源。

取消不证明 native 工作已结束，操作负责安全清理。保存持有独立租约；操作和清理失败一起保留，且清理错误可能发生在文件替换成功后。重试前检查实际文件。

尚未提交或失败的显示范围变更不影响捕获映射。参见[帧契约](frame-pipeline.md#freeze-snapshot-and-save)和[菜单目标](menus.md)。
