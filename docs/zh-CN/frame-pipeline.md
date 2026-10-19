# 帧与像素

[English](../en/frame-pipeline.md)

本指南说明如何从 CPU 内存创建图像、提交帧，以及通过租约读取原始像素。请先运行[完整 Viewer 示例](getting-started.md)；以下代码假定该 Viewer 仍然存活。

## 选择 CPU 帧格式

[入门示例](getting-started.md)使用 Gray8：每像素一个字节，默认显示范围为 0–255。其他 CPU 图像沿用相同的创建和提交 API，只需使用与源数据布局一致的描述符。下面仅演示高位深灰度和彩色透明度带来的额外要点，假定已有存活 `viewer`，并导入 `Fizzy.ImageViewer` 与 `Fizzy.ImageViewer.Frames`；应用中也应按首个示例检查提交结果。

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

以下章节说明存储生命周期、显示和原始像素查询契约。

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

## 输入与生命周期

`ImageFrame.Copy` 同步复制，原存储仍属调用方。`TakeOwnership` 接管不可变存储，最后一个帧或租约释放时恰好执行一次 release，包括构造验证失败；构造和清理均失败时聚合，原错误在前，不重试 release。宽高为正，正 stride 至少覆盖有效行；支持行填充，输入单平面、从上到下，Gray16/Gray32Float 为小端。不支持的格式明确失败。

存储字节数不等于语义通道数：Gray16 两字节一个通道，Bgr32 四字节三个通道，末字节为填充；Bgra32/Pbgra32 四通道，分别为直接/预乘 alpha。

`SubmitFrameAsync` 在取消、冻结和关闭时也消耗帧。禁止重复提交、修改或释放已转移存储。每个 `FrameLease` 独立持有引用，幂等释放；启动异步工作前获取独立租约，不得一边读取内存一边释放同一租约。

提交自身释放错误进入 `FrameSubmitResult.Error`；Viewer 当前帧或重绘租约的释放错误进入 `DisposeAsync`；独立租约释放错误直接传播。release 在最后释放引用的线程同步执行，可能是提交线程、工作线程或 STA，生产者负责线程亲和并保持短小，不存在专用释放线程。

Viewer 保留原帧供查询，冻结可能无限期保留；相机池必须允许这种保留，或适配器主动复制。只读接口不能阻止其他持有者修改内存，不可变性是生产者契约。

## 提交与显示

每个 Viewer 一个 STA 和渲染循环，最多一帧处理中、一帧等待，新帧替换等待帧。状态为 `Committed`、`Superseded`、`Frozen`、`Cancelled`、`Closed`、`Failed`；同时检查 Status 和 Error，丢帧也可能有清理错误，清理失败不改变提交或丢弃原因。

取消在 UI 提交前尽力生效，提交后不撤销。等待帧在出队、替换、冻结或关闭时结束，不为每帧注册取消回调。Committed 表示 UI 已更换源和当前帧，不证明显示器已呈现。FrameId 在 Viewer 内单调递增。写像素后先执行单次 OnCommitted，再发 FrameCommitted；回调中的租约仅借用至回调结束。`FrameCommitted` 使用 `EventHandler<FrameInfo>`，sender 为公开的 Viewer 或 ImageViewerControl；订阅规则见[事件订阅契约](public-api.md#事件订阅)。查询通知独立调度，禁止回调阻塞等待另一提交；异步工作自行获取租约。订阅异常隔离。

CPU 呈现器复用两个 WriteableBitmap，后台转换，先写未显示位图再切换。WPF 原生八位布局保留，映射灰度和显示快照使用 Pbgra32。GPU 使用 D3DImage。图像坐标始终是原图像素，不随 DPI 改变。

图像原点是左上边界；整数像素索引与连续位置不同。像素 `(x,y)` 的中心是 `(x+0.5,y+0.5)`，
可用 `PixelCoordinate.ToImageCenter()` 获取。算法结果的适配见[绘图坐标](drawing-layers.md#坐标尺寸与输入)。
视口使用 WPF 高质量位图缩放，减少缩小时细小特征的丢失。显示重采样不改变原始像素或查询结果，
也不保证亚像素显示精度。

DisplayRange 读取最新请求；等待 `SetDisplayRangeAsync` 观察重绘失败。默认 Gray8 0–255、Gray16 0–65535、Gray32Float 0–1。边界有限且严格递增，范围外裁剪，NaN/负无穷黑、正无穷白。存在帧时等该显示版本成功提交；新请求或关闭取消旧等待，无帧时只配置后续提交。当前 GPU 帧拒绝非空范围，拒绝前不改请求。禁止 STA 同步等待。

第一帧正在准备时仍可能提交其之前捕获的映射，需要重绘时先等待第一帧。失败更新的请求值仍可读，重试需显式再次设置。快照使用最后成功提交的映射与版本。

## 像素访问与测量

像素协议、CPU 解码、坐标区域、结果及租约属于 Frames；Imaging 负责显示转换、采样和调度，Frames 不依赖 Imaging。`AcquireCurrentFrame()` 返回调用方拥有的租约。`TryGetCpuPixels` 只访问已有 CPU 内存，GPU 返回 false，没有通用 Data 属性。静态 `CpuFramePixelReader.TryReadPixel` 越界返回 false，范围内 GPU 读取抛出 NotSupportedException，不隐式下载。查询与测量结果通过 `SourceFrame` 标识原始帧，区域像素使用区域局部坐标。

跨后端使用租约的 `ReadPixelsAsync`、`ComputeRegionStatisticsAsync`、`ReadRegionAsync`。输入坐标用 ReadOnlyMemory，结果保留顺序、原单位和格式；每次异步调用自行获取租约直至结束。CPU 可在调用线程同步完成，直接调用不承诺工作线程；Viewer 调度器及快照自行安排离开 STA。

操作和清理错误一起保留，原错误优先；区域结果仅在源租约清理成功后交给调用方，否则释放不可交付区域。PixelSamplesResult 和 RegionStatistics 复制输入并暴露只读集合，提供者返回数组后不得修改或复用。结果携带查询租约自己的 FrameInfo；未提交或派生图像 FrameId 为 0，无源时间戳，提交前获取的租约仍未提交。

RegionPixels/ImageSnapshot 的 SourceFrame、Region 描述原输入。AcquirePixels 返回描述裁剪或转换后像素的独立租约，使用局部坐标，不冒充源帧身份。RegionPixels 自有 CPU 图像需释放，仅由经过验证的区域读取创建。不支持能力明确失败，无下载回退。

CPU 支持 Gray8/Gray16/Gray32Float/Rgb24/Bgr24/Bgr32/Bgra32/Pbgra32/Rgba32。统计按语义 Gray、R/G/B[/A] 顺序返回 count/min/max/mean，忽略非有限浮点，无有限值时 count 为 0、统计为 null；预乘值仍预乘，Bgr32 填充不参与。格式和通道数必须匹配，显示范围不限制原始查询值。

一个调度器同一时刻一批：STA 捕获几何，后台查询，STA 发布。最早到期请求决定下一批是单个 ROI 或合并点/线采样；未选中者不持租约，下次捕获新状态。几何变化保留节流截止时间。

点和线使用半开覆盖 `[0, Width) × [0, Height)`；线裁剪后端点 floor，Bresenham 保留输入方向及端点像素。右/下独占边缘上的线无样本；交叉端点可夹到最后像素。极大有限坐标使用精确裁剪，但不能恢复调用前丢失的精度。ROI floor 左上、ceil 右下再与图像相交，空区域不查询。

QueryOptions 默认 Pixel/Line 30 Hz、Region 10 Hz、MaxResultAge 100 ms，均为正，频率需可表示至少一个 TimeSpan tick 且小于最大间隔。年龄从帧/几何捕获计时，不是相机采集时间。订阅、会话、描述符及测量几何版本检查始终存在；新帧可接受年龄范围内旧帧结果，暂停且未变帧结果不因时间自动过期。

QueryMetrics.Batches 计完成批次含失败，不等于发布数；ExpiredResults 计发布时超龄结果，不计后来清空的显示值；LastDurationMilliseconds 从捕获到后端完成，含工作调度、不含 STA 发布等待。

像素 HUD 限频为 min(10, PixelQueryRateHz)，每次完成（含失败）后完整等待间隔。移动合并最新目标但不重置间隔，同会话可发布之前采样位置，坐标和值一起更新。目标或帧变化时旧显示最多保留采样开始后 300 ms，之后显示 `—`；静止结果不过期，失败立即 `—`。离开图像、禁用或无帧时隐藏并拒绝旧结果。

HUD 无背景，固定文化数字格式，每值至少七字符，长值不截断，窄视口裁剪不换行。每次捕获用当前容器指针和变换重算坐标，即使无鼠标移动；隐藏/卸载或容器外无目标。FrameId 不作为 HUD 标签。

## 冻结、快照与保存

打开右键菜单会冻结当前帧，清除等待输入并使冻结前的在途提交失效。关闭菜单恢复新输入。菜单租约固定图像、显示映射和 ROI，处理 Click/Closed 顺序；保存动作在打开文件对话框前获得独立租约。

捕获、编码和所有权详见[快照](snapshots.md)。导出使用已提交映射，不使用尚未提交的显示范围请求。

## 关闭与集成

先停止并等待生产者，再等待 Viewer 释放；不能阻塞 Viewer STA。已接纳提交排空至释放和公开结果完成，包括提交线程上的替换清理。独立租约可越过 Viewer 生命周期；共享完成及失败结果见 [Viewer 关闭协议](public-api.md#线程与窗口生命周期)。

应用适配器负责将自身图像转换为 `ImageFrame`：借用 CPU 存储用 `Copy`，不可变自有存储用 `TakeOwnership`，已就绪 GPU 表面用 `D3D9ImageFrame.TakeOwnership`。转换、设备兼容性及适配器测试归消费者负责。

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

需提供兼容 WPF、非多重采样、位于 `D3DPOOL_DEFAULT` 的 `IDirect3DSurface9` 渲染目标，像素为预乘透明度的 `D3DFMT_A8R8G8B8`。转移前完成 GPU 写入并解除计算映射。描述符和 `IFramePixelSource` 描述原始像素，表面必须与这些像素的标准显示转换一致。在 `releaseResources` 执行前，设备、表面和查询资源必须保持有效且不可变；工厂失败也会转移所有权。实现适配器前先阅读 [GPU 契约](#gpu-表面与显式像素查询)。

## GPU 表面与显式像素查询

`D3D9ImageFrame.TakeOwnership` 接管已就绪且不可变的 GPU 表面，包括验证失败时的清理责任。只要还有租约，就不能覆写表面。描述符和像素源描述原始像素，显示表面必须匹配其尺寸及标准显示转换。

这是受信任的 native 边界，非零指针不证明兼容。生产者提供非多重采样、`D3DPOOL_DEFAULT` 中的 `D3DFMT_A8R8G8B8` 渲染目标，使用预乘 alpha。设备必须兼容 WPF；CUDA 扩展使用带 `D3DCREATE_MULTITHREADED` 和 `D3DCREATE_FPU_PRESERVE` 的 D3D9Ex 设备。生产者负责设备兼容性、native 元数据验证和同步；转移前完成全部写入并解除计算映射，仅提交 GPU 工作不足以交付。

设备、表面和原始查询资源持续有效至最终释放回调。本入口不表达自定义颜色映射、方向或显示范围；Display 快照从原始像素重建标准映射，不读取显示表面。GPU 帧共享最新帧调度、FrameId、冻结/取消和提交回调。D3DImage 在 STA 创建和绑定；正常显示路径不调用 CPU 转换或像素提供者。前缓冲恢复会重新绑定保留的当前表面；CPU 帧切回 WriteableBitmap。

`IFramePixelSource` 独立提供异步点采样、统计和区域读取。查询失败影响交互，不影响显示，也不能触发隐式整图下载。区域读取交付具有请求尺寸和源格式的独立 CPU `ImageFrame`；坐标从区域原点开始，`FrameLease` 在 `RegionPixels` 中补充来源。帧释放回调负责提供者的资源，库不另行释放提供者。

每个提供者方法在返回或抛出前都必须结束对源资源的访问，包括取消路径。GPU 工作已经入队时，取消发布也不能提前释放缓冲区和租约。独立租约、直接读取、查询调度器和快照可以并发调用同一提供者的不同方法；提供者必须支持并发或串行化后端。调度器的单批和导出的容量限制并不串行化所有提供者调用。

GPU 帧不支持非 null `DisplayRange`。生产者自行转换为支持的帧契约。驱动重置与 WPF 前缓冲可用性不同；库不重建设备，也不提供软件显示回退。

前缓冲恢复回调失败后，呈现器停止接纳 GPU 表面，不从 WPF 事件回调抛出或重试。该错误保留至 `Viewer.DisposeAsync`，切回 CPU 也不丢失；不追溯修改已返回的提交结果。表面附加失败后停止使用该呈现器；只有确认分离成功才能释放租约。无法确认时，呈现器和可能仍附加的租约隔离保留至进程结束，以防 native 释放后访问，并作为失败报告。

可选 [CUDA 组件](cuda.md)提供 Gray8、Bgr24、Rgb24、Bgra32、Pbgra32、Rgba32 的显示转换和原始像素查询；应用仍负责帧与执行环境适配，核心 WPF 程序集没有 CUDA 依赖。
