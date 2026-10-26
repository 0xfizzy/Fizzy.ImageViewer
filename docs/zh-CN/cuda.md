# CUDA 集成

[English](../en/cuda.md)

可选 `Fizzy.ImageViewer.Cuda11`、`.Cuda12` 或 `.Cuda13` 包支持 CUDA 驻留的 Gray8、Bgr24、Rgb24、Bgra32、Pbgra32、Rgba32，
提供 WPF 显示转换及原始像素查询。消费者实现小型执行桥接；核心和扩展均不依赖特定应用或计算运行时。

## 创建帧

为独立保留的不可变源图实现 `ICudaFrameSource`，`Descriptor` 描述原始像素：

```csharp
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Cuda;

ImageFrame frame = await CudaImageFrame.TakeOwnershipAsync(source, cancellationToken);
var result = await viewer.SubmitFrameAsync(frame);
if (result.Error is { } error) throw error;
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
| RegisterResource | GPU 访问前登记 ICudaResource，返回独立所有权令牌 |
| WaitForGpuCompletion | 不受取消影响地证明设备访问完成，但不结束 operation |

描述符 stride 可以不同于设备 pitch，实际设备行距由 scope 提供，两者尺寸、格式、像素值一致。
扩展验证尺寸、支持格式和存储范围，不验证任意指针安全性。不接受 CPU 输入、负 stride、多平面或外部不透明句柄。

帧工厂在调用时立即接管源帧；`RegisterResource` 仅在成功时转移清理责任，登记失败仍由登记调用方清理。

桥接拥有最终完成和退役流程。operation 与独立令牌共同保留资源，仅在最终安全释放时调用 `ICudaResource.Release`。
清理失败须保留资源及 context、通过运行时失败渠道报告并隔离，不重试破坏性清理。native 调用或等待失败不证明
设备访问已经结束，桥接必须排空或隔离已登记的输入、临时内存及外部资源，才能允许回收。

执行前取消不启动 GPU；入队后取消只停止交付，不能绕过完成。查询必须等待成功后读取 host 字节，作用域退出前
复制为独立结果。表面发布还要等待整个 ExecuteAsync 成功。消费者异常直接传播；扩展 native 错误使用包含类型化 `CudaErrorDomain` 和原始 native code 的 `CudaException`。操作与自有资源清理错误同时保留。

## 格式与传输成本

Gray8 扩为不透明 BGRA；Bgr24/Rgb24 保留源通道并补 alpha。Bgra32/Rgba32 原始读取保留直通 alpha，显示按
`(color * alpha + 127) / 255` 预乘，RGBA 另换通道。Pbgra32 直接复制到映射纹理，跳过转换 kernel 和临时 BGRA
缓冲。原始采样/统计不反预乘；Raw TIFF 保留原值，Display 快照与表面使用同一标准映射。

显示无图像 CPU 回读，但仍有显存复制，其他格式另需转换缓冲。每个表面独立分配且不可变，没有纹理池；同一 CUDA
context 的活动表面共享 D3D9Ex device。仍要求设备/适配器兼容并支持 CUDA/D3D9 互操作。

查询只下载请求点、紧凑统计（每语义通道 count/min/max/sum 共 32 字节）或指定区域。gather 每 65,536 点分批，
保留顺序；统计输出 Gray 或 R/G/B[/A] 顺序，小 ROI 只使用必要归约块，上限 256。完整快照显式请求全图。
没有隐式整图缓存或下载回退。CudaDiagnostics.QueryMetrics 统计扩展传输及活动查询回调，不代表运行时实际分配
或隔离字节。`EnqueuedQueries` 和 `EnqueuedResultBytes` 统计成功返回的 native 查询，每个 gather 分块单独计数，不承诺完整统计失败前的部分入队。`ActiveQueries` 和 `ActiveResultBytes` 覆盖查询执行及托管结果复制；归零不证明 GPU 已完成或隔离区为空。GetLiveSurfaceCount() 会加载 native DLL，包含保留的 native 表面 owner。

不提供高位深 CUDA 映射、预乘 RGBA、Bayer、自定义 LUT/显示范围、D3D11/12 输入、软件回退或设备重建。
WPF 附加与恢复遵循[帧契约](frame-pipeline.md#gpu-表面与显式像素查询)。

## 构建与部署

按宿主 CUDA 大版本选择一个完整包：`Fizzy.ImageViewer.Cuda11`、
`Fizzy.ImageViewer.Cuda12` 或 `Fizzy.ImageViewer.Cuda13`。三个包使用相同的
`Fizzy.ImageViewer.Cuda` 命名空间和程序集。不得直接或通过传递依赖混用多个变体，
也不得与旧 `Fizzy.ImageViewer.Cuda` 包混用；构建会明确拒绝这些组合。
已经发布的包保持不可变，新版本使用明确的大版本包名。

| 包 | 构建 Toolkit | Host 编译器 | 默认架构 |
| --- | --- | --- | --- |
| Fizzy.ImageViewer.Cuda11 | 11.8.0 | MSVC 14.38（v143） | SM 7.5 |
| Fizzy.ImageViewer.Cuda12 | 12.6.3 | MSVC 14.38（v143） | SM 7.5 |
| Fizzy.ImageViewer.Cuda13 | 13.0.2 | MSVC 14.38（v143） | SM 7.5 |

这些是构建基线，不代表所有小版本、GPU 和执行特性都已认证。Native ABI 1 使用标准
CUDA 句柄及调用方持有的存储；宿主必须保证上下文、流、内存和完成协议有效。
运行仍要求兼容的 NVIDIA 驱动、GPU 及 CUDA/D3D9Ex 适配器。CUDA 与 MSVC Runtime
静态链接，因此包使用者无需安装本库的 Toolkit 或 Visual Studio。应用启动时不下载或编译依赖。

```powershell
dotnet add package Fizzy.ImageViewer.Cuda11
```

普通托管开发不需要 CUDA 工具链。本机 native 开发只保留一个 Toolkit，默认 11.8。
安装 VS 2022 Build Tools、MSVC 14.38 和 Windows SDK 10.0.22621.0，
不需要完整 IDE 或 v142。安装脚本还会选择标准 MSVC x64/x86 工具组件：
即使通过 `VsDevCmd.bat` 加载环境，nvcc 仍要求其 `vcvarsall.bat` 安装目录结构。
该依赖会带入 VS 2022 默认工具集，但 CUDA 编译仍显式选择 14.38。
缺少组件时，在管理员 PowerShell 中运行安装选项：

```powershell
./scripts/Setup-Cuda.ps1 -InstallBuildTools
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release
# 显式执行硬件测试，要求合适的本机 GPU 和桌面环境。
./scripts/Test-Cuda.ps1 -CudaMajor 11 -Configuration Release -RunHardwareTests
```

Setup 将固定版本 CMake 3.31.8 和 Ninja 1.12.1 准备到 `artifacts/cuda/tools`，
不修改 NVIDIA 驱动或持久环境变量。构建脚本在子进程中选择 MSVC 14.38，使用 Ninja，
不依赖 CUDA Visual Studio 插件。通过 `-CudaRoot` 指定已有 Toolkit。CI 另使用
`Setup-Cuda.ps1 -CudaMajor 12 -DownloadToolkit -InstallBuildTools`，只下载经过
SHA-256 校验的 NVIDIA 编译组件。公共矩阵为 `eng/cuda-matrix.json`，发布要求精确匹配。

Native 输出位于 `build/<Configuration>/native/cuda<major>/`，中间文件位于
`build/obj/cuda/<major>/<Configuration>/`，native 测试位于
`build/<Configuration>/tests/Cuda<major>/`。报告及临时包位于 `artifacts/cuda/<major>/`。
Native DLL 附有独立构建记录，标识源码、编译器和哈希；打包拒绝缺失、陈旧、不匹配或被修改的输入：

```powershell
./scripts/Build-Cuda.ps1 -CudaMajor 11 -Configuration Release
dotnet pack src/Fizzy.ImageViewer.Cuda -c Release -p:CudaMajor=11 -o artifacts/packages
```

GitHub Actions 编译三个变体及 native 测试，在 .NET 8/10 运行托管 CUDA 测试，并验证
包内容和消费者发布输出。普通 Windows runner 不执行 GPU 测试；矩阵通过只证明构建与
打包兼容，不证明 CUDA 12/13 的硬件或真实显示。显式本机硬件模式运行有超时限制的
native 测试，覆盖六种格式、alpha、带 padding 的 stride、gather、ROI、统计、D3D9Ex
表面读回和清理。表面读回不等于真实屏幕呈现验收；环境不满足时不得静默跳过已请求的硬件测试。
发布及恢复见[发布指南](releasing.md#可选-cuda-包)。
