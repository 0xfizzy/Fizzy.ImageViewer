# 快照

[English](../en/snapshots.md)

捕获当前已提交图像的整图或区域，并独立于 Viewer 保存。快照导出图像像素，不是包含缩放、叠加或 HUD 的视口截图。

## 捕获与保存

以下示例假定 `viewer` 存活，且允许替换目标 `path` 的内容：

```csharp
using Fizzy.ImageViewer.Snapshots;

using var snapshot = await viewer.CaptureSnapshotAsync(SnapshotKind.Display);
await snapshot.SaveAsync(path, SnapshotEncoding.Png);
```

完整用法见[导出示例](../../samples/MeasurementsAndExport/Program.cs)。

## 目标与所有权

`CaptureSnapshotAsync(Raw|Display)` 在调用时固定当前已提交帧，返回独立不可变存储，不选取最新的未提交帧。区域重载接受非空且完全位于图像内的 `PixelRegion`；没有图像时抛出 `InvalidOperationException`。`Raw` 保留原值，`Display` 应用已提交映射，不包含标记、标签、HUD 或视口缩放。等待中或失败的显示范围更新不会影响快照。

每个 Viewer 一次执行一个区域读取，最多再接纳一个等待捕获。超出容量时，在获取帧租约前同步抛出 `InvalidOperationException`。等待中的捕获已固定调用时的帧，不会改选新帧；完成、取消或失败释放槽位。ROI 保存菜单在文件对话框前同时固定帧和整数区域。

已接纳捕获拥有完成所需依赖，可在 Viewer 关闭后完成。调用方负责释放结果。快照的 `SourceFrame` 和 `Region` 保留原始来源；`AcquirePixels()` 返回区域局部坐标下的独立租约，可越过快照和 Viewer 生命周期。对该租约查询时得到派生像素的信息，不冒用源帧身份。

Raw 捕获直接接管独立读取的 CPU 区域，不再次复制。区域释放回调在最终像素引用释放后执行，包括快照释放后仍保留的外部租约。交付前其他所属资源清理失败时，会释放无法交付的候选快照，并同时保留操作与清理失败。

## 编码、取消与失败

`ImageSnapshot.SaveAsync(path, encoding, token)` 自行持有租约，在目标目录写临时文件，编码成功后才替换目标。替换前及 TIFF 行间检查取消；WPF 的 PNG/JPEG/BMP 编码本身不可中断。

Display 支持 PNG、JPEG、BMP 和 TIFF；JPEG 将透明部分合成到黑色。Raw 只允许 TIFF，不静默降低高位深精度。编码、取消和清理错误会共同保留，操作错误在前。清理可能在目标替换成功后失败，因此任务失败不证明文件未改变，重试前应检查文件。

右键 **Save Image As...** 通过文件类型选择显示 PNG/JPEG/BMP 或原始 TIFF。选中已完成矩形测量时，**Save Region Image As...** 提供同样格式。菜单一次只执行一个保存动作；应用独立创建的快照可并发保存，Viewer 关闭后仍可使用。

TIFF 使用未压缩 Classic TIFF，Gray16 标记为 UINT，Gray32Float 为 IEEEFP；左上原点、RGB 通道顺序，并正确标记关联/非关联 alpha。Bgr32 的填充字节被省略。超过保守 Classic TIFF 大小上限时拒绝，不支持 BigTIFF。

取消不证明 native 工作已经停止；操作会保留依赖直到安全完成。另见[菜单目标](menus.md#捕获图像目标)。
