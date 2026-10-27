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

自有视口将图像、绘图、测量和 HUD 内容裁剪在其布局边界内，缩放、平移和调整尺寸后同样适用，无需在公共控件上设置 `ClipToBounds`。点击视口会从宿主 UI 或另一个查看器取回键盘焦点；Escape 和 Delete 作用于获得焦点的查看器交互。每个控件独立保留工具、选择和图层状态。

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

提交立即转移帧所有权，包括拒绝和取消。布局完成后通过 `FitToViewport()` 适应图像。绘图、测量、HUD、像素查询、快照和菜单遵循与独立窗口相同的契约；线剖面测量仍可打开独立曲线窗口。静态图像和低频切图可使用下述绑定源；实时帧流仍推荐显式 `SubmitFrameAsync`。

`Accepted` 仅表示接收，不表示显示完成或缓冲区可回收。通过 `FrameCommitted` / `OnCommitted` 和 `DisplayFailed` 观察后续显示结果；已接受帧可能被替换而从未显示。可复用的生产者缓冲区应使用 `Copy`，或等待已转移存储的最终释放回调后再回收，见[帧契约](frame-pipeline.md)。

`Visible` 控制控件自身 WPF 显隐，false 映射到 `Hidden`；XAML 绑定或需要 `Collapsed` 时使用继承的 `Visibility`。`IsVisible` 包含祖先显隐影响；`IsMinimized` 读取当前所属窗口，无窗口时为 false。这些属性遵循 WPF 线程亲和性。
只有已加载、有效可见且宿主未最小化时才进行显示工作；暂停期间继续接收，只保留最新等待帧。非当前 Tab 由宿主控制显隐，不进行专门检测；恢复显隐或加载后主动显示最新输入。

## 绑定静态图像

在宿主 UI 线程创建 ViewModel，并暴露稳定的 `Fizzy.ImageViewer.Frames.ImageBindingSource`：

```csharp
public ImageBindingSource Image { get; } = new();

// Run on the host UI thread, after producing an immutable buffer.
public void ShowImage(FrameDescriptor descriptor, byte[] pixels)
{
    using var frame = ImageFrame.TakeOwnership(descriptor, pixels, static () => { });
    Image.SetFrame(frame);
    // Do not write pixels again. SetFrame retained an independent reference.
}
```

```xml
<viewer:ImageViewerControl x:Name="ImageViewer" ImageSource="{Binding Image}" />
```

ViewModel 拥有并释放 `Image`；宿主拥有控件并等待其释放。`SetFrame` 获取独立引用，不消费输入帧，不复制像素。
每个已关联控件通过现有帧管线独立保留同一份不可变存储，最后一个引用释放才回收存储。
池化或 native 存储必须提供实际释放回调；上述空回调仅适用于托管数组。

源必须在创建它的 UI 线程上创建、更新和释放。同一源可服务同一 Dispatcher 上的多个控件，不支持跨 Dispatcher。
后台可准备 `ImageFrame`，但必须保留它，直到等待的 Dispatcher `SetFrame` 调用完成，再释放调用方自己的帧。
不要为此源使用后台 setter 或 `Binding.IsAsync`。显式帧提交保留原有跨线程能力。

绑定源对象，不绑定可释放的 `ImageFrame` 或 `FrameLease`。WPF 可能延迟读取 ViewModel 属性：延迟读取看到源的当前图像，
源已释放则看到空，不依赖旧帧令牌保持有效直到绑定回调。可以在 UI 线程替换 ViewModel 的源属性并释放旧源；
`PropertyChanged` 返回不是所有权交接确认。通常保持稳定源并调用 `SetFrame` 即可，无须额外属性通知。

| 操作 | 结果 |
| --- | --- |
| 再次赋值同一个源 | 不因依赖属性重复赋值而重新提交 |
| 再次调用 `SetFrame`，包括传入同一帧 | 向每个已关联控件发起一次新接收 |
| `SetFrame(null)` | 释放源的图像引用；控件保留已显示图像 |
| 将 `ImageSource` 设为 null 或清除绑定 | 解除该控件订阅，不释放源，不清空画面 |
| 释放源 | 清空源并通知控件，拒绝后续更新；延迟绑定看到空 |
| 卸载、重新加载控件 | 保留源关联和最新等待帧；重载时恢复显示 |
| 释放控件 | 解除其绑定和源关联并排空管线；其他控件和源保持独立 |

清空源或解绑不撤销已接受的输入：等待中或在途帧仍可能显示。这些操作均不清空测量。
依赖属性保留的已释放源是空源对象，不是失效的所有权令牌。已释放控件不可复用；停止后赋非 null 源会抛出 `ObjectDisposedException`。

`ImageSourceSubmitResult` 是只读依赖属性，包含最近一次绑定接收结果，源为空或解绑时为 null。
可绑定其 `Status` 和 `Error` 提供 UI 反馈。结果属于每个控件，不属于源；`Accepted` 不等于已显示。
冻结期间更新被拒绝，不自动重试；需要新接收时再次调用 `SetFrame`。
通过 `FrameCommitted`、`DisplayFailed` 观察后续显示结果，等待 `DisposeAsync` 观察管线清理失败。
源自身清理失败在通知所有控件后由 `SetFrame` 或 `Dispose` 传播，新值保持安装，不重试清理；外部租约的清理仍由持有者观察。

绑定与显式提交共享同一个最新帧接收顺序。显式提交可以替换绑定图像；不变的绑定不会重新覆盖它。
后续 `SetFrame` 或绑定到另一个非空源会再次提交。`SubmitFrameAsync` 仍立即消费输入，包括拒绝和取消。

## WPF 命令

按钮、菜单或 `KeyBinding` 可使用 `ImageViewerCommands.FitToViewport`、`ActivateMeasurementTool` 和 `EndInteraction`，
无需 MVVM 框架。位于控件外的工具栏应显式指定目标；不指定 `CommandTarget` 时遵循 WPF 键盘焦点路由。
不同控件不共享工具会话。

```xml
<Button Content="Fit" Command="{x:Static viewer:ImageViewerCommands.FitToViewport}"
        CommandTarget="{Binding ElementName=ImageViewer}" />
<Button Content="Measure" Command="{x:Static viewer:ImageViewerCommands.ActivateMeasurementTool}"
        CommandParameter="{Binding ToolId}" CommandTarget="{Binding ElementName=ImageViewer}" />
<Button Content="End" Command="{x:Static viewer:ImageViewerCommands.EndInteraction}"
        CommandTarget="{Binding ElementName=ImageViewer}" />
```

`ToolId` 为内置 `MeasurementToolIds` 值或在目标控件注册的自定义工具 ID。
`CanExecute` 只检查已存储的注册元数据和图层准入，不创建会话，不调用自定义工具代码。
注册、交互、图层输入策略、显示与布局、控件可用性变化时刷新可执行状态。

| 命令 | 额外可执行条件 |
| --- | --- |
| `FitToViewport` | 存在已显示图像，视口布局非空 |
| `ActivateMeasurementTool` | 非空白字符串指向已注册工具；测量层可见、允许命中且不在清理 |
| `EndInteraction` | 存在正在创建或编辑的会话；保留已完成测量和已应用编辑 |

卸载、隐藏、禁用、最小化或停止后，三个命令均不可执行。激活命令的无效参数不可执行；Fit/End 忽略参数。
执行时再次检查，不可用则不操作。实际工具执行的异常传播至 WPF 命令调用方；直接方法保留原有异常契约。
命令调用和状态查询遵循 WPF UI 线程亲和性。

库不安装全局快捷键。示例在静态图像控件上配置 Ctrl+F、Ctrl+L 和 Ctrl+E。
已有 Escape 输入和内置菜单复用同一个交互协调器；菜单保留捕获的注册和会话身份，避免延迟点击影响替代会话。
End 不增加删除、清空、保存或释放操作。

## 线程与临时卸载

库提供的查看方法按需调度到控件 Dispatcher；绑定源、依赖属性、命令及继承的布局属性和视觉树操作仍遵循 WPF 线程规则。通知在所属 UI 线程执行；查看器级 `MeasurementCompleted`、`MeasurementChanged`、`MeasurementRemoved` 事件在嵌入时的 sender 为控件，独立窗口模式为 `Viewer`；`IMeasurement` 上的事件以测量对象自身为 sender。不要同步等待依赖该 Dispatcher 的工作。

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

示例分别提供绑定静态图像和显式实时提交页签。Next image 在后台准备像素，并在 UI 线程更新 ViewModel 的源；
实时页签使用显式提交。按钮、菜单和快捷键演示三个命令及显式目标。
切换页签验证卸载重载，在流式显示时关闭：宿主等待两个生产任务结束，释放 ViewModel 的源，再等待两个控件释放后关闭。
不需要相机或 GPU。
