<p align="center"><img src="assets/icon-readme.png" alt="Fizzy.ImageViewer" width="108" height="108"></p>

# Fizzy.ImageViewer

[![NuGet](https://img.shields.io/nuget/v/Fizzy.ImageViewer.svg?style=flat)](https://www.nuget.org/packages/Fizzy.ImageViewer/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=flat)](LICENSE)
[![.NET 8 / 10](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4.svg?style=flat)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-blue.svg?style=flat)](docs/zh-CN/getting-started.md)

[English](README.md) | 简体中文

用于 Windows/.NET 8 和 .NET 10 WPF 应用的图像显示、绘制、测量与像素查询库。可将 `ImageViewerControl` 嵌入 WPF 窗口，或使用独立窗口与 STA 的 `Viewer`；相机采集、设备控制和业务适配由应用负责。

访问[文档网站](https://0xfizzy.github.io/Fizzy.ImageViewer/)，阅读双语指南和 API 参考。

## 安装

```powershell
dotnet add package Fizzy.ImageViewer
```

同一个 NuGet 包包含 `net10.0-windows` 和 `net8.0-windows` 程序集。项目使用其中一个目标（或兼容的新目标），并设置 `<UseWPF>true</UseWPF>`。

## 快速开始

启用隐式 using 后，以下完整控制台入口会显示灰度图像，并等待用户关闭窗口：

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;

await using var viewer = new Viewer(showWindow: false);
var closed = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
viewer.Closed += (_, _) => closed.TrySetResult();
var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await viewer.SubmitFrameAsync(ImageFrame.Copy(
    new FrameDescriptor(640, 480, 640, FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != FrameSubmitStatus.Committed)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
viewer.Show();
await closed.Task;
```

提交会立即转移帧所有权，包括拒绝和取消的情况。`Committed` 表示 UI 已接纳该帧，不代表物理屏幕已显示。

## 核心功能

- 缩放、平移、适应窗口与无边框显示
- 图形叠加、批量标记与屏幕坐标 HUD 文字
- 点、长度、矩形 ROI 和线剖面测量，支持交互编辑
- 原始像素查询、整图/区域快照与自定义菜单
- 有界最新帧提交，以及可选 CUDA 集成

## 阅读入口

| 需求 | 文档 |
| --- | --- |
| 嵌入 WPF 窗口 | [嵌入指南](docs/zh-CN/embedding.md) |
| 运行第一个程序 | [入门](docs/zh-CN/getting-started.md) |
| 显示实时图像流 | [实时流集成](docs/zh-CN/live-streaming.md) |
| 绘制或测量 | [绘制与 HUD](docs/zh-CN/drawing-layers.md) |
| 使用交互测量 | [测量与交互](docs/zh-CN/measurements.md) |
| 查找组件或 API | [指南目录](docs/zh-CN/index.md) |

[双语入口](docs/index.md)

## 开发入口

构建与测试见[验证](docs/zh-CN/validation.md)，模块边界见[架构](docs/zh-CN/architecture.md)。维护工作参见[文档维护](docs/zh-CN/documentation.md)与[发布 SOP](docs/zh-CN/releasing.md)。

## 许可证

[MIT](LICENSE)。
