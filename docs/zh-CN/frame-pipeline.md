# 帧管线

[English](../frame-pipeline.md)

<a id="input-and-lifetime"></a>

## 输入与生命周期

`ImageFrame.Copy` 同步复制，原存储仍属调用方。`TakeOwnership` 接管不可变存储，最后一个帧或租约释放时恰好执行一次 release，包括构造验证失败；构造和清理均失败时聚合，原错误在前，不重试 release。宽高为正，正 stride 至少覆盖有效行；支持行填充，输入单平面、从上到下，Gray16/Gray32Float 为小端。不支持的格式明确失败。

存储字节数不等于语义通道数：Gray16 两字节一个通道，Bgr32 四字节三个通道，末字节为填充；Bgra32/Pbgra32 四通道，分别为直接/预乘 alpha。

`SubmitFrameAsync` 在取消、冻结和关闭时也消耗帧。禁止重复提交、修改或释放已转移存储。每个 `FrameLease` 独立持有引用，幂等释放；启动异步工作前获取独立租约，不得一边读取内存一边释放同一租约。

提交自身释放错误进入 `FrameSubmitResult.Error`；Viewer 当前帧或重绘租约的释放错误进入 `DisposeAsync`；独立租约释放错误直接传播。release 在最后释放引用的线程同步执行，可能是提交线程、工作线程或 STA，生产者负责线程亲和并保持短小，不存在专用释放线程。

Viewer 保留原帧供查询，冻结可能无限期保留；相机池必须允许这种保留，或适配器主动复制。只读接口不能阻止其他持有者修改内存，不可变性是生产者契约。

<a id="submission-and-display"></a>

## 提交与显示

每个 Viewer 一个 STA 和渲染循环，最多一帧处理中、一帧等待，新帧替换等待帧。状态为 `Committed`、`Superseded`、`Frozen`、`Cancelled`、`Closed`、`Failed`；同时检查 Status 和 Error，丢帧也可能有清理错误，清理失败不改变提交或丢弃原因。

取消在 UI 提交前尽力生效，提交后不撤销。等待帧在出队、替换、冻结或关闭时结束，不为每帧注册取消回调。Committed 表示 UI 已更换源和当前帧，不证明显示器已呈现。FrameId 在 Viewer 内单调递增。写像素后先执行单次 OnCommitted，再发 FrameCommitted；回调中的租约仅借用至回调结束。查询通知独立调度，禁止回调阻塞等待另一提交；异步工作自行获取租约。订阅异常隔离。

CPU 呈现器复用两个 WriteableBitmap，后台转换，先写未显示位图再切换。WPF 原生八位布局保留，映射灰度和显示快照使用 Pbgra32。GPU 使用 D3DImage。图像坐标始终是原图像素，不随 DPI 改变。

DisplayRange 读取最新请求；等待 `SetDisplayRangeAsync` 观察重绘失败。默认 Gray8 0–255、Gray16 0–65535、Gray32Float 0–1。边界有限且严格递增，范围外裁剪，NaN/负无穷黑、正无穷白。存在帧时等该显示版本成功提交；新请求或关闭取消旧等待，无帧时只配置后续提交。当前 GPU 帧拒绝非空范围，拒绝前不改请求。禁止 STA 同步等待。

第一帧正在准备时仍可能提交其之前捕获的映射，需要重绘时先等待第一帧。失败更新的请求值仍可读，重试需显式再次设置。快照使用最后成功提交的映射与版本。

<a id="pixel-access-and-measurements"></a>

## 像素访问与测量

像素协议、CPU 解码、坐标区域、结果及租约属于 Frames；Imaging 负责显示转换、采样和调度，Frames 不依赖 Imaging。`AcquireCurrentFrame()` 返回调用方拥有的租约。`TryGetCpuPixels` 只访问已有 CPU 内存，GPU 返回 false，没有通用 Data 属性。CpuFramePixelReader 越界返回 false，范围内 GPU 读取抛出 NotSupportedException，不隐式下载。

跨后端使用租约的 `ReadPixelsAsync`、`ComputeRegionStatisticsAsync`、`ReadRegionAsync`。输入坐标用 ReadOnlyMemory，结果保留顺序、原单位和格式；每次异步调用自行获取租约直至结束。CPU 可在调用线程同步完成，直接调用不承诺工作线程；Viewer 调度器及快照自行安排离开 STA。

操作和清理错误一起保留，原错误优先；区域结果仅在源租约清理成功后交给调用方，否则释放不可交付区域。PixelSamplesResult 和 RegionStatistics 复制输入并暴露只读集合，提供者返回数组后不得修改或复用。结果携带查询租约自己的 FrameInfo；未提交或派生图像 FrameId 为 0，无源时间戳，提交前获取的租约仍未提交。

RegionPixels/ImageSnapshot 的 SourceFrame、Region 描述原输入。AcquirePixels 返回描述裁剪或转换后像素的独立租约，使用局部坐标，不冒充源帧身份。RegionPixels 自有 CPU 图像需释放，仅由经过验证的区域读取创建。不支持能力明确失败，无下载回退。

CPU 支持 Gray8/Gray16/Gray32Float/Rgb24/Bgr24/Bgr32/Bgra32/Pbgra32。统计按语义 Gray、R/G/B[/A] 顺序返回 count/min/max/mean，忽略非有限浮点，无有限值时 count 为 0、统计为 null；预乘值仍预乘，Bgr32 填充不参与。格式和通道数必须匹配，显示范围不限制原始查询值。

一个调度器同一时刻一批：STA 捕获几何，后台查询，STA 发布。最早到期请求决定下一批是单个 ROI 或合并点/线采样；未选中者不持租约，下次捕获新状态。几何变化保留节流截止时间。

点和线使用半开覆盖 `[0, Width) × [0, Height)`；线裁剪后端点 floor，Bresenham 保留输入方向及端点像素。右/下独占边缘上的线无样本；交叉端点可夹到最后像素。极大有限坐标使用精确裁剪，但不能恢复调用前丢失的精度。ROI floor 左上、ceil 右下再与图像相交，空区域不查询。

QueryOptions 默认 Pixel/Line 30 Hz、Region 10 Hz、MaxResultAge 100 ms，均为正，频率需可表示至少一个 TimeSpan tick 且小于最大间隔。年龄从帧/几何捕获计时，不是相机采集时间。订阅、会话、描述符及测量几何版本检查始终存在；新帧可接受年龄范围内旧帧结果，暂停且未变帧结果不因时间自动过期。

QueryMetrics.Batches 计完成批次含失败，不等于发布数；ExpiredResults 计发布时超龄结果，不计后来清空的显示值；LastDurationMilliseconds 从捕获到后端完成，含工作调度、不含 STA 发布等待。

像素 HUD 限频为 min(10, PixelQueryRateHz)，每次完成（含失败）后完整等待间隔。移动合并最新目标但不重置间隔，同会话可发布之前采样位置，坐标和值一起更新。目标或帧变化时旧显示最多保留采样开始后 300 ms，之后显示 `—`；静止结果不过期，失败立即 `—`。离开图像、禁用或无帧时隐藏并拒绝旧结果。

HUD 无背景，固定文化数字格式，每值至少七字符，长值不截断，窄视口裁剪不换行。每次捕获用当前容器指针和变换重算坐标，即使无鼠标移动；隐藏/卸载或容器外无目标。FrameId 不作为 HUD 标签。

<a id="freeze-snapshot-and-save"></a>

## 冻结、快照与保存

菜单打开冻结当前帧，清等待帧并使冻结前在途提交失效；关闭后恢复新输入。菜单租约跨 Click/Closed 次序保留帧和显示设置；保存弹窗前获取独立租约。输入排空或 Viewer 关闭释放菜单租约。

快照调用时固定帧，Raw 保留原值，Display 应用已提交映射，不含叠加、HUD 或视口缩放。整帧也显式请求完整区域，GPU 读取不缓存回源。一次读、一次等待，更多请求在获取租约前同步拒绝；完成、取消、失败释放容量。ROI 同时固定帧与整数区域。

Raw 接管独立 CPU 区域，不额外复制；最后像素引用才触发区域 release，包括越过快照的独立租约。交付前其他清理失败会清理候选快照并保留全部错误。

保存先在目标目录写临时文件，编码成功后替换。替换前及 TIFF 行间检查取消，WPF PNG/JPEG/BMP 编码本身不可中断。Display 支持四种格式，JPEG 黑底；Raw 仅 TIFF，不默默降精度。清理错误可能发生在替换成功之后。菜单整图和选中已完成矩形区域都可保存，菜单一次一个保存，独立快照可并发保存且关闭后仍可用。

TIFF 为无压缩 Classic TIFF，Gray16 UINT、Gray32Float IEEEFP、左上方向、RGB 顺序、正确 alpha 标签，不写 Bgr32 填充；超保守尺寸限制拒绝，不支持 BigTIFF。参见[快照指南](snapshots.md)。

<a id="shutdown-and-integrations"></a>

## 关闭与集成

使用 await using 或等待 DisposeAsync，不能阻塞 STA。关闭停止接纳，排空已接纳提交、渲染和查询至公开结果完成并等待线程退出，包括提交线程上的替换清理。重复等待同一结果；清理失败不变为成功，不重做。Viewer 不等外部独立租约，也不提前回收其存储；已接纳且独立持依赖的导出可继续。

<a id="gpu-surfaces-and-explicit-pixel-queries"></a>

## GPU 表面与显式像素查询

GPU 输入提供不可变 BGRA IDirect3DSurface9、所有者及显式像素源。呈现不触发像素读取，CPU 显示范围不应用于 GPU。查询仅下载明确请求数据，返回独立 CPU 存储；提供者支持协议要求的并发、格式与所有权。最后源租约释放才清理查询资源；不能证明 D3D 安全分离时隔离保留而不重用。真实 GPU/切换仍需设备验证。

<a id="internal-composition"></a>

## 内部组成

FramePipeline 管等待帧、当前帧、冻结代次、请求/已提交显示设置及结果；FramePresentation 管后台准备、STA 呈现和后端资源；MenuSnapshotSession 管菜单目标与保存互斥；SnapshotCapture 管串行区域读取和独立快照。

CommittedViewLease 原子捕获帧租约、显示范围和版本，不拥有导出同步。管线共享 ViewerLifetime 接纳门，昂贵上传在门外，附加图像和发布当前帧在门内；提交、冻结/恢复和呈现释放由 STA 串行。仅图像提交发帧通知，纯重绘不发。关闭先停管线和交互、释放菜单、测量与层，再等待渲染、查询及 STA。
