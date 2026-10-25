# 嵌入 WPF 窗口

[English](../en/embedding.md)

图像需要放入已有窗口、Grid 或 TabControl 时，使用 `ImageViewerControl`；需要独立窗口及独立 STA 时，使用 `Viewer`。需要完整查看能力时借用 `IViewerCapabilities`，仅需部分功能时使用窄接口；拥有者保留 `IViewer` 负责释放。只有独立窗口 `Viewer` 实现 `IViewerWindow`。

## 放置与配置

项目启用 `UseWPF`，目标框架为 `net8.0-windows` 或 `net10.0-windows`，并引用 Fizzy.ImageViewer。在窗口中放置控件：

```xml
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:viewer="clr-namespace:Fizzy.ImageViewer;assembly=Fizzy.ImageViewer">
    <Grid>
        <viewer:ImageViewerControl x:Name="ImageViewer" />
    </Grid>
</Window>
```

无参构造支持 XAML；代码中可向 `new ImageViewerControl(logger)` 传入 `ILogger<ImageViewerControl>`，null 表示禁用日志。必须在宿主 STA 上创建控件。首次 `Loaded` 前即可提交帧，即使从未加载也必须释放。`Content` 由库管理，替换会被拒绝；使用常规 WPF 布局属性，不替换内容或模板。

在 `InitializeComponent` 后调用与 `Viewer` 相同的查看方法：

```csharp
ImageViewer.HudLabelText = "Camera";
var pixels = new byte[640 * 480];
Array.Fill(pixels, (byte)128);
var result = await ImageViewer.SubmitFrameAsync(
    Fizzy.ImageViewer.Frames.ImageFrame.Copy(
        new(640, 480, 640, Fizzy.ImageViewer.Frames.FramePixelFormat.Gray8), pixels));
if (result.Error is { } error) throw error;
if (result.Status != Fizzy.ImageViewer.Frames.FrameSubmitStatus.Accepted)
    throw new InvalidOperationException($"Initial frame: {result.Status}");
```

提交立即转移帧所有权，包括拒绝和取消。布局完成后通过 `FitToViewport()` 适应图像。绘图、测量、HUD、像素查询、快照和菜单遵循与独立窗口相同的契约；线剖面测量仍可打开独立曲线窗口。不提供帧绑定依赖属性或查看器命令，由应用适配器调用方法。

`Visible` 控制控件自身 WPF 显隐，false 映射到 `Hidden`；XAML 绑定或需要 `Collapsed` 时使用继承的 `Visibility`。`IsVisible` 包含祖先显隐影响；`IsMinimized` 读取当前所属窗口，无窗口时为 false。这些属性遵循 WPF 线程亲和性。
只有已加载、有效可见且宿主未最小化时才进行显示工作；暂停期间继续接收，只保留最新等待帧。非当前 Tab 由宿主控制显隐，不进行专门检测；恢复显隐或加载后主动显示最新输入。

## 线程与临时卸载

库提供的查看操作按需调度到控件 Dispatcher；继承的布局属性及视觉树操作仍遵循 WPF 线程规则。通知在所属 UI 线程执行；嵌入时测量事件 sender 为控件，独立窗口模式为 `Viewer`。不要同步等待依赖该 Dispatcher 的工作。

嵌入控件共享应用 UI 线程。应用 UI 繁忙会延迟呈现和交互，同线程多个控件也共享这部分开销。后台帧准备和查询仍独立执行。独立窗口 `Viewer` 在自己的 STA 上创建同一个控件；两种封装均不额外增加帧复制或提交队列。

`Unloaded`（包括切换 Tab）会结束当前交互并释放鼠标捕获，但保留帧和已完成测量；卸载期间可继续提交帧。同一 Dispatcher 上重新挂载即可继续使用。卸载不等于最终释放，不支持将实例移动到另一个 Dispatcher。

## 异步关闭

嵌入方先停止并等待生产者，再在宿主 Dispatcher 仍运行时等待 `ImageViewer.DisposeAsync()`。普通窗口应取消首次 `Closing`，异步清理后，在本次处理函数返回后再次请求关闭。[完整示例](../../samples/EmbeddedViewer/MainWindow.xaml.cs)实现此模式，防止重复关闭，并报告生产者及清理失败。

重复释放共享同一完成任务及成功或失败结果。释放停止接纳、排空已接受的帧和查询工作并清理自有资源，不关闭宿主窗口或 Dispatcher。独立帧租约继续有效。已释放的控件是终态，需要新建实例，不能通过重新加载恢复。

Dispatcher 提前关闭时，控件在 `ShutdownStarted` 中尽力停止和安全清理，释放结果报告生命周期失败；这不能代替正常等待释放。无法安全释放的 native 资源保持隔离，不释放或复用。卸载交互清理失败会记录日志，并保留至最终释放结果。

## 应用端点

通过应用自有接口隔离 pipeline 与 ViewModel 的依赖，见[应用集成](application-integration.md)和 [ApplicationEndpoint 示例](../../samples/ApplicationEndpoint/App.xaml.cs)。简单窗口仍适合直接调用。

## 运行示例

```powershell
dotnet run --project samples/EmbeddedViewer
```

示例向 XAML 控件持续提交合成像素。用工具栏适应图像或测量，切换 Tab 验证卸载与重载，并在流式显示时关闭以验证异步结束；不需要相机或 GPU。
