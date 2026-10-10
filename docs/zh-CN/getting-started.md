# 入门

[English](../getting-started.md)

本指南从显示单张图像开始，演示有代表性的 CPU 帧和 GPU 帧入口，再介绍如何高效地持续送帧。

## 创建 Viewer

使用 Windows、.NET 8、`net8.0-windows` 项目并设置 `<UseWPF>true</UseWPF>`。安装包：

```powershell
dotnet add package Fizzy.ImageViewer
```

Viewer 创建独立 STA；控制台程序无需自行创建应用 Dispatcher。启用隐式 using 后，以下代码可作为完整的控制台入口：

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
viewer.Title = "My first image";

var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");

viewer.Show();
await closed.Task;
// await using waits for shutdown and reports cleanup failures.
```

在 `Show()` 前配置并订阅事件。提交会立即转移所有权，即使操作被取消或 Viewer 已关闭也一样：不要重复提交同一帧，也不要释放已转移的存储。`Committed` 表示 UI 已接受该帧，不代表显示器已显示。应用应同时检查 `Status` 和 `Error`，丢弃的帧也不例外。

`Closed` 是通知，不证明渲染和查询已排空。先停止并等待生产者结束，再等待 `DisposeAsync`；不要在 Viewer 回调中同步等待这些操作。Viewer 对应一次窗口生命周期，关闭后应新建实例。

## 选择 CPU 帧格式

首个示例使用 Gray8：每像素一个字节，默认显示范围为 0–255。其他 CPU 图像沿用相同的创建和提交 API，只需使用与源数据布局一致的描述符。下面仅演示高位深灰度和彩色透明度带来的额外要点，沿用首个示例的 `viewer` 和命名空间；应用中也应按首个示例检查提交结果。

`FrameDescriptor(width, height, stride, format)` 描述自上而下的单平面像素。**Stride 是相邻行起点之间的字节数**，包含行尾填充，必须为正且不小于一行有效像素的字节数。缓冲区至少提供 `descriptor.RequiredBytes` 字节。负步长、多平面和 Bayer 数据需要在消费者中转换。`ImageFrame.Copy` 返回前已完成复制，随后可以复用源数组或相机缓冲区。

### 高位深灰度：设置显示范围

Gray16 每像素使用两个小端字节。如果传感器将 12 位数值存入 16 位容器，可在首次提交前将显示范围设置为 0–4095。这只调整显示对比度，不改变原始像素查询或原始导出的数值；默认范围为 0–65535。

```csharp
ushort[] values = { 0, 1024, 2048, 4095 };
var pixels = new byte[values.Length * 2];
for (int i = 0; i < values.Length; i++)
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
        pixels.AsSpan(i * 2, 2), values[i]);
await viewer.SetDisplayRangeAsync(new Fizzy.ImageViewer.Imaging.GrayDisplayRange(0, 4095));
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(4, 1, 8, FramePixelFormat.Gray16), pixels));
```

相机已提供小端字节且步长有效时，直接传入这些字节，无需逐值重新打包。紧凑打包的 10/12 位相机格式需要先解包。Gray32Float 每像素使用四个小端 IEEE 754 字节，默认显示范围为 0–1；温度、深度等数据应按原始单位设置范围。

调整已有图像时，先等待其提交，再等待 `SetDisplayRangeAsync` 以观察重绘完成或失败。传入 `null` 可恢复格式默认范围。

### 彩色：匹配通道顺序与透明度

选择与源字节一致的格式。例如，下面的 Bgra32 帧依次包含一个半透明红色像素和一个不透明绿色像素：

```csharp
byte[] pixels = { 0, 0, 255, 128,  0, 255, 0, 255 };
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(2, 1, 8, FramePixelFormat.Bgra32), pixels));
```

Rgb24/Bgr24 每像素三字节，通道顺序与名称一致；Bgr32 每像素四字节，第四字节是填充而非透明度。Bgra32/Rgba32 使用非预乘透明度。Pbgra32 的颜色已乘以透明度，上面的半透明红色应写为 `{ 0, 0, 128, 128 }`。不要把非预乘字节标记为预乘，也不要重复预乘。

完整的格式、显示和原始像素查询契约见[帧管线](frame-pipeline.md)。

## 转移自有 CPU 内存

相机 SDK 会覆写的借用缓冲区应使用 `Copy`。只有能在最后一个帧引用释放前控制存储时，才使用 `TakeOwnership`。它省去输入复制，**不代表**省去 CPU 显示准备或 WPF 位图写入。

```csharp
var descriptor = new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8);
var pixels = new byte[descriptor.RequiredBytes];
Array.Fill(pixels, (byte)128); // Finish all writes before transfer.
var frame = ImageFrame.TakeOwnership(descriptor, pixels, static () => { });
var result = await viewer.SubmitFrameAsync(frame);
// Never write pixels again. This managed array needs no external release action.
```

已有内存池或相机缓冲区所有者时，传入准确的内存切片，并提供归还该缓冲区的释放回调。不要在提交后或等到 `Committed` 后立即释放所有者：当前图像、查询和独立租约仍可能保留该帧，冻结的 Viewer 还可能无限期保留它。`TakeOwnership` 在最终释放时调用一次释放回调，构造验证失败也会调用；不要再在 catch/finally 中重复归还缓冲区。回调应短小、不抛异常，并能安全地在最终释放引用的线程上执行，该线程可能是 Viewer STA。生产者无法满足这些保留时间与线程要求时，应使用 `Copy`。

## 提交 GPU 帧

GPU 帧沿用同一套 `ImageFrame` 提交 API，但需要生产者适配；CPU 数组或任意 native 指针都不能直接当作 GPU 帧。

### 已就绪的 D3D9 表面

以下适配方法接收生产者已创建并完成同步的资源：

```csharp
static ValueTask<FrameSubmitResult> SubmitSurfaceAsync(
    Viewer viewer, FrameDescriptor originalDescriptor, nint readySurface,
    IFramePixelSource originalPixels, Action releaseResources)
{
    var frame = D3D9ImageFrame.TakeOwnership(
        originalDescriptor, readySurface, originalPixels, releaseResources);
    return viewer.SubmitFrameAsync(frame);
}
```

需提供兼容 WPF、非多重采样、位于 `D3DPOOL_DEFAULT` 的 `IDirect3DSurface9` 渲染目标，像素为预乘透明度的 `D3DFMT_A8R8G8B8`。转移前完成 GPU 写入并解除计算映射。描述符和 `IFramePixelSource` 描述原始像素，表面必须与这些像素的标准显示转换一致。在 `releaseResources` 执行前，设备、表面和查询资源必须保持有效且不可变；工厂失败也会转移所有权。实现适配器前先阅读 [GPU 契约](frame-pipeline.md#gpu-表面与显式像素查询)。

### CUDA 源图像

安装可选的 `Fizzy.ImageViewer.Cuda` 包，并由消费者实现 `ICudaFrameSource` 后，扩展组件会创建表面和像素查询提供者：

```csharp
static async Task<FrameSubmitResult> SubmitCudaAsync(
    Viewer viewer, Fizzy.ImageViewer.Cuda.ICudaFrameSource source,
    CancellationToken cancellationToken)
{
    var frame = await Fizzy.ImageViewer.Cuda.CudaImageFrame.TakeOwnershipAsync(
        source, cancellationToken);
    return await viewer.SubmitFrameAsync(frame, cancellationToken: cancellationToken);
}
```

工厂立即拥有 `source`，失败或取消也不例外。支持的输入为 Gray8、Rgb24、Bgr24、Bgra32、Pbgra32 和 Rgba32。显示避免 CPU 回读，但仍有设备内存复制。GPU 帧不支持通过 `SetDisplayRangeAsync` 设置非空范围。执行桥接、部署和设备要求见 [CUDA 集成](cuda.md)。

## 高性能使用 ImageViewer

### 在提交前限制工作量

Viewer 同时处理一个提交，只保留最新的一个等待帧；被替换的等待帧返回 `Superseded`。这适合实时显示，不是无损录像队列。

拉取式生产者可像 [LiveOverlays](../../samples/LiveOverlays/Program.cs) 一样，等待每次提交后再生成下一帧，自然限制生产工作量。推送式相机回调则应在应用侧设置有界的“只保留最新帧”交接，由工作线程提交并观察每个结果；提交前丢弃的自有帧也必须释放。避免不断增长的 `Task.Run` 队列、无界通道或提交任务列表：Viewer 内部的有界队列不能限制上游已经分配的工作。关闭时停止接纳并排空生产者。

等待提交只能观察提交或丢弃结果，以及该提交的释放错误，不能证明相机缓冲区可复用。对于已转移的存储，只有最终释放回调能确定这一点。统计各状态数量和错误，避免在热路径回调中逐帧记录成功日志。

### 减少复制与转换

- 保留源数据已支持的格式和步长，不要仅为显示而将 Gray8 扩展成 BGRA，或交换 RGB/BGR 通道。支持的原生 8 位 CPU 布局会保留；Gray16、Gray32Float、Rgba32 和调整显示范围的 Gray8 需要显示转换。
- 使用 `Copy` 时可复用生产者缓冲区；使用 `TakeOwnership` 时应转移独立拥有、不可变的缓冲区。`Copy` 内部已租用内存，生产者侧额外池化应先测量分配和保留情况。绝不能提前覆写已转移的内存。
- 在一次窗口生命周期内复用 Viewer。尺寸和显示格式稳定时，可复用内部两张 CPU WriteableBitmap。只在需要时修改显示范围，不要逐帧请求重绘。
- 原始像素已在 GPU 上时，用支持的 GPU 适配器避免整图往返传输。对于 CPU 输入，GPU 适配并不自动更快，仍需考虑分配、同步与复制成本。

### 批量更新叠加层，限制查询开销

为每组独立更新的绘图创建一个句柄，在帧流期间保留它，结果变化时调用 `Replace`：

```csharp
using var markers = viewer.Layers.Markers.Add(
    Array.Empty<Fizzy.ImageViewer.Drawing.DrawingElement>());
// In the update loop, replace the whole batch using current detection results.
markers.Replace(new Fizzy.ImageViewer.Drawing.DrawingElement[]
{
    new Fizzy.ImageViewer.Drawing.CircleElement(
        new System.Windows.Point(100, 100), 5, System.Windows.Media.Brushes.Red)
});
```

数千次独立 `Add` 会创建数千个 visual 并产生大量调度；提交一个集合只创建一个 visual，但批量替换仍有几何构造和绘制成本。昂贵的检测或叠加更新应独立于图像帧率限频。要求叠加内容与帧一致时，遵循[实时叠加协议](drawing-layers.md#实时相机叠加的消费者要求)。

像素查看、剖面和区域统计通常不必按相机帧率执行。例如可在显示窗口前设置较低的查询频率：

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

### 保持回调短小，测量完整链路

`OnCommitted` 和 `FrameCommitted` 在 Viewer STA 执行。不要在其中编码、等待设备、执行重分析、同步等待任务，或再次提交并等待结果。需要异步处理时，在回调返回前获取独立租约，工作完成后释放；不能直接保留回调借用的租约。此类后台工作同样需要有界。

分别测量生产者的转换与复制成本、托管分配、提交状态数量、查询耗时和叠加更新成本。提交完成不等于物理显示延迟。对调整的路径运行[帧、绘图和查询基准](validation.md#线剖面分配)，再在目标机器验证交互和显示。合成基准不能证明持续相机吞吐量或混合 DPI 表现。

## 完整示例

在仓库根目录执行；这些人工检查会打开 WPF 窗口：

```powershell
dotnet run --project samples/BasicViewer
dotnet run --project samples/LiveOverlays
dotnet run --project samples/MeasurementsAndExport
```

- [BasicViewer](../../samples/BasicViewer/Program.cs)：显示复制的灰度像素并等待关闭。
- [LiveOverlays](../../samples/LiveOverlays/Program.cs)：模拟帧流、复用批量句柄、先结束生产再释放。
- [MeasurementsAndExport](../../samples/MeasurementsAndExport/Program.cs)：独立点测量会话、通知和 `artifacts/samples/` 下的 PNG 输出。

`./scripts/Test-Samples.ps1` 只构建、不打开窗口。导出可接受输出目录参数，选择不含需保留的 `snapshot.png` 的目录。

继续阅读[帧](frame-pipeline.md)、[生命周期](public-api.md)和[测量](measurements.md)。
