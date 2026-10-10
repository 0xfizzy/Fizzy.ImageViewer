# 入门

[English](../getting-started.md)

使用 Windows、.NET 8、`net8.0-windows` 项目并设置 `UseWPF=true`。Viewer 创建独立 STA；控制台程序无需自行创建应用 Dispatcher 即可调用门面。

1. 使用 `Viewer(showWindow: false)`，保证所有退出路径执行异步释放。
2. 在 `Show()` 前订阅 `Closed` 并配置窗口。
3. 用 `ImageFrame.Copy` 创建不可变输入，只提交一次；即使 Viewer 已关闭，提交也转移所有权。
4. 等待提交或丢弃结果，再显示窗口。
5. 关闭时停止生产者，等待其结束及 `DisposeAsync`；不要在 STA 回调中同步等待。

`Closed` 是通知，不证明渲染和查询已排空。Viewer 对应一次窗口生命周期，关闭后应新建实例。

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
