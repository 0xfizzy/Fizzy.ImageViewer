<p align="center"><img src="assets/icon-readme.png" alt="Fizzy.ImageViewer icon" width="108" height="108"></p>

# Fizzy.ImageViewer

[English](README.md)

用于 .NET 8 WPF 应用的图像显示、绘制、测量、交互编辑、HUD 和像素查询库。每个 Viewer 拥有独立 STA 窗口，帧提交使用有界队列。

## 安装

```powershell
dotnet add package Fizzy.ImageViewer
```

消费者目标必须为 `net8.0-windows` 或兼容的新目标，设置 `<UseWPF>true</UseWPF>`。

## 快速开始

```csharp
using Fizzy.ImageViewer;
using Fizzy.ImageViewer.Frames;
using Fizzy.ImageViewer.Measurements;

await using var viewer = new Viewer();
var pixels = new byte[640 * 480];
var frame = ImageFrame.Copy(new FrameDescriptor(640, 480, 640,
    FramePixelFormat.Gray8), pixels);
var result = await viewer.SubmitFrameAsync(frame);
```

提交立即转移所有权；等待返回提交或丢弃结果，不代表物理屏幕已显示。一次处理一帧，仅保留最新等待帧。测量独立读取原始像素，租约和原始像素访问类型位于 `Fizzy.ImageViewer.Frames`。

大量标记应作为一个集合提交：

```csharp
using var markers = viewer.Layers.Markers.Add(
    Enumerable.Range(0, 10000).Select(i =>
        new Fizzy.ImageViewer.Drawing.CircleElement(
            new System.Windows.Point(i % 100 * 10, i / 100 * 10),
            3, System.Windows.Media.Brushes.Red)));
viewer.ActivateMeasurementTool(MeasurementToolIds.Length); // Independent interactive measurement layer.
```

参见[测量](docs/zh-CN/measurements.md)、[公共 API 与生命周期](docs/zh-CN/public-api.md)及[绘制层](docs/zh-CN/drawing-layers.md)。应用适配器拥有 sealed `Viewer`；需要先配置时使用 `showWindow: false`，配置订阅后 `Show()`，停止时等待 `DisposeAsync()`。实时流复用绘制句柄并替换内容，遵循指南中的消费者要求。

## 功能

- 独立 STA 窗口与公共 API 调度
- 低分配渲染及有界帧队列
- 缩放、平移、适应窗口和无边框
- 线、文本、准星、矩形、圆及 HUD
- 集合绘制、可配置图层和交互测量
- 模型驱动自定义测量、编辑、查询与通知
- 可扩展菜单及像素查询

## 构建与文档

阅读[组件指南](docs/zh-CN/index.md)、[完整示例](docs/zh-CN/getting-started.md)及[架构](docs/zh-CN/architecture.md)。[在线文档](https://0xfizzy.github.io/Fizzy.ImageViewer/)从 v1.2.0 起按 `/v{version}/` 留存，`/dev/` 对应主分支。

```powershell
./scripts/Build-Docs.ps1
./scripts/Serve-Docs.ps1
dotnet build -c Release
dotnet pack -c Release -o ./artifacts
```

构建、发布和恢复入口见[验证](docs/zh-CN/validation.md)、[文档维护](docs/zh-CN/documentation.md)及[发布 SOP](docs/zh-CN/releasing.md)。发布使用 GitHub Actions 和 NuGet Trusted Publishing。

## 许可证

[MIT](LICENSE)。
