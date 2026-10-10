# CUDA 集成

[English](../cuda.md)

可选 `Fizzy.ImageViewer.Cuda` 包支持 CUDA 驻留的 Gray8、Bgr24、Rgb24、Bgra32、Pbgra32、Rgba32，
提供 WPF 显示转换及原始像素查询。消费者实现小型执行桥接；核心和扩展均不依赖特定应用或计算运行时。

## 创建帧

为独立保留的不可变源图实现 `ICudaFrameSource`，`Descriptor` 描述原始像素：

```csharp
ImageFrame frame = await CudaImageFrame.CreateAsync(source, cancellationToken);
await viewer.SubmitFrameAsync(frame);
```

工厂立即接管非空 source，包括验证失败和取消。返回帧自带 `IFramePixelSource`，消费者不再实现另一套查询适配。
提交照常转移帧所有权。最终帧/租约引用释放源图及独立显示资源令牌。独立租约可越过 Viewer 生命周期，
桥接执行服务必须在这些租约及读取结束前始终可用。

## 执行桥接

`ExecuteAsync` 在源图就绪后、Viewer STA 之外恰好调用一次同步回调，支持并发或自行串行后端。
不得保留或重入回调。回调借用 `ICudaExecutionScope`：

| 成员 | 消费者责任 |
| --- | --- |
| Context / Stream | 标准 CUDA 句柄，不是运行时私有对象；context 保活至外部资源清理完成 |
| InputPointer / InputByteLength / RowStride | 导出设备地址前登记只读使用，存储覆盖访问的各行 |
| AllocateScratch | 提供设备或锁页主机临时内存，由操作保留到安全完成 |
| RetainResource | GPU 访问前登记 ICudaResource，返回独立所有权令牌 |
| WaitForCompletion | 不受取消影响地证明设备访问完成，但不结束 operation |

描述符 stride 可以不同于设备 pitch，实际设备行距由 scope 提供，两者尺寸、格式、像素值一致。
扩展验证尺寸、支持格式和存储范围，不验证任意指针安全性。不接受 CPU 输入、负 stride、多平面或外部不透明句柄。

桥接拥有最终完成和退役流程。operation 与独立令牌共同保留资源，仅在最终安全释放时调用 `ICudaResource.Release`。
清理失败须保留资源及 context、通过运行时失败渠道报告并隔离，不重试破坏性清理。native 调用或等待失败不证明
设备访问已经结束，桥接必须排空或隔离已登记的输入、临时内存及外部资源，才能允许回收。

执行前取消不启动 GPU；入队后取消只停止交付，不能绕过完成。查询必须等待成功后读取 host 字节，作用域退出前
复制为独立结果。表面发布还要等待整个 ExecuteAsync 成功。消费者异常直接传播；扩展 native 错误使用包含 domain
和 native code 的 CudaException。操作与自有资源清理错误同时保留。

## 格式与传输成本

Gray8 扩为不透明 BGRA；Bgr24/Rgb24 保留源通道并补 alpha。Bgra32/Rgba32 原始读取保留直通 alpha，显示按
`(color * alpha + 127) / 255` 预乘，RGBA 另换通道。Pbgra32 直接复制到映射纹理，跳过转换 kernel 和临时 BGRA
缓冲。原始采样/统计不反预乘；Raw TIFF 保留原值，Display 快照与表面使用同一标准映射。

显示无图像 CPU 回读，但仍有显存复制，其他格式另需转换缓冲。每个表面独立分配且不可变，没有纹理池；同一 CUDA
context 的活动表面共享 D3D9Ex device。仍要求设备/适配器兼容并支持 CUDA/D3D9 互操作。

查询只下载请求点、紧凑统计（每语义通道 count/min/max/sum 共 32 字节）或指定区域。gather 每 65,536 点分批，
保留顺序；统计输出 Gray 或 R/G/B[/A] 顺序，小 ROI 只使用必要归约块，上限 256。完整快照显式请求全图。
没有隐式整图缓存或下载回退。CudaDiagnostics.QueryMetrics 统计扩展传输及活动查询回调，不代表运行时实际分配
或隔离字节。LiveSurfaces 会加载 native DLL，包含保留的 native 表面 owner。

不提供高位深 CUDA 映射、预乘 RGBA、Bayer、自定义 LUT/显示范围、D3D11/12 输入、软件回退或设备重建。
WPF 附加与恢复遵循[帧契约](frame-pipeline.md#gpu-surfaces-and-explicit-pixel-queries)。

## 构建与部署

核心 WPF 项目无需 CUDA 工具链；可选托管项目也不初始化 CUDA。CUDA 包将匹配的
Fizzy.ImageViewer.Cuda.Native.dll 放在 runtimes/win-x64/native，不兼容 ABI 明确失败。
native ABI 1 定义于 native/Cuda/imageviewer_cuda.h，只接受标准 CUDA 句柄及调用方存储。

native 构建需要 Windows x64、CUDA Toolkit 11.8 和 MSVC v142，默认 SM 7.5 及 PTX，可按部署 GPU 调整架构。
无需外部运行时 SDK 或同级检出：

```powershell
./scripts/Build-Cuda.ps1 -Configuration Release -Architectures '75'
dotnet build extensions/Fizzy.ImageViewer.Cuda -c Release
dotnet pack extensions/Fizzy.ImageViewer.Cuda -c Release -o artifacts/packages
```

输出在 artifacts/cuda/Release；缺少 native DLL 时 CUDA 打包失败。核心包不包含 CUDA 二进制或源码。
执行需要兼容 NVIDIA 驱动，应用启动时不自动下载或编译依赖。

构建脚本仅编译 native 测试。单独获得硬件授权后执行
`ctest --test-dir artifacts/cuda -C Release --output-on-failure -L hardware`，验证表面像素、六种格式、alpha、
padding、gather 和 ROI 边界，不证明物理延迟、远程桌面、多适配器迁移或驱动重置恢复。
