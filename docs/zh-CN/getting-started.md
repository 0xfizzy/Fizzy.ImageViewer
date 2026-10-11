# 入门

[English](../en/getting-started.md)

本指南帮助你运行第一个图像窗口，再按需要选择组件指南。

## 创建 Viewer

使用 Windows 和 .NET 8。创建控制台项目并使用下面的项目文件，再安装包：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

```powershell
dotnet add package Fizzy.ImageViewer
```

Viewer 创建独立 STA；控制台程序无需自行创建应用 Dispatcher。启用隐式 using 后，以下代码可作为完整的控制台入口：

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
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

## 基本操作

滚轮缩放，中键平移；右键菜单提供测量和图像导出。也可在示例的 `viewer.Show()` 前加入以下调用：

```csharp
viewer.HudLabelText = "My image";
viewer.IsPixelInfoEnabled = true;
viewer.ActivateMeasurementTool(
    Fizzy.ImageViewer.Measurements.MeasurementToolIds.Length);
```

点击两个点测量图像像素单位的长度；Escape 结束创建或编辑。读取结果见[测量](measurements.md)，添加标记见[绘制](drawing-layers.md)，保存图像见[快照](snapshots.md)。

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

## 进阶集成

继续阅读 [CPU/GPU 帧](frame-pipeline.md)、[CUDA 集成](cuda.md)或[实时流所有权与性能](live-streaming.md)。
