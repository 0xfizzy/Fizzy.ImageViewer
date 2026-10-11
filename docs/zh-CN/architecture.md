# 架构

[English](../en/architecture.md)

修改资源所有权或模块依赖前阅读本页。库每个 Viewer 拥有一个独立 WPF STA；消费者提交不可变帧，通过门面、绘图和测量句柄操作。

## 源码组织

| 目录 | 职责 |
| --- | --- |
| Viewer | 公共门面、Host 组合、窗口和生命周期 |
| Frames / Pixels | 不可变存储、描述符、租约、提交、像素源协议、CPU 解码与独立结果 |
| Rendering | 提交队列、已提交状态、CPU 与 D3D 呈现 |
| native/Cuda | 可选 CUDA/D3D9Ex kernel，借用标准 CUDA context 和 stream 句柄 |
| Imaging / Queries | 线采样、显示转换、查询设置及共享调度/执行 |
| Layers | 公共层组合、变换、输入和生命周期容器 |
| Drawing | 元素、标记层、句柄、批量绘制 |
| Measurements | 集合、句柄、选项、快照、通知、样式与生命周期 |
| Measurements/Queries | 查询种类、几何兼容、请求、客户端与结果 |
| Measurements/Geometry | 封闭不可变几何族 |
| Measurements/Tools | 工具契约、注册、创建上下文；BuiltIn 为内置工具 |
| Measurements/Editing | 编辑会话和控制点 |
| Measurements/Presentation | 叠加、视觉投射、标签、曲线窗口 |
| Interaction | 测量协调、WPF 输入及共享捕获 |
| Viewport | 图像坐标、缩放与平移 |
| Hud | 屏幕坐标文字、像素查询与显示状态 |
| Snapshots | 独立整帧/区域捕获及编码 |
| Menus | 菜单契约、动作、注册、绑定和固定目标 |

### 放置规则

按拥有行为和生命周期的功能归属放代码，包括接口、枚举和控件；第二个调用者不意味着建通用 Helpers。公共顶层和独立内部类型用同名文件，宿主专用类型嵌套，紧密内部查询请求/结果族可聚合。只为既有职责建子目录，不按可见性或类型种类分层。

Frames/Pixels 保留 Frames 命名空间，Measurements 的 Geometry/Tools/Queries 保留公共 Measurements 命名空间；内置工具为 Measurements.BuiltIn。测试按能力同样组织，保留命名空间以兼容过滤器，辅助类靠近调用方，基准留 tools。托管库项目统一放在 src/：核心项目位于 src/Fizzy.ImageViewer/，可选 CUDA 后端位于 src/Fizzy.ImageViewer.Cuda/。上表功能路径相对于核心项目，native/Cuda 相对于仓库根目录；测试、示例、工具和原生构建保留在托管库项目之外。

Viewer 按 Window、Frames、Display、Drawing、Measurements、HUD、Queries、Snapshots、Lifetime 分 partial，但不是独立所有者。IViewer 聚合所属功能的借用接口，直接由同一实例实现，只有聚合负责异步释放和全局 Layers；接口不是安全隔离。审查返回对象、集合元素及事件参数，不只看直接成员。

可见性由 C# 和审阅 API 基线约束；测试可通过 Viewer.Host 组合内部。适配器拥有 sealed Viewer，必要时隐藏创建，统一用户关闭与显式释放流程。组件指南描述当前契约。

## 帧与图像依赖

可选 `Fizzy.ImageViewer.Cuda` 程序集仅引用核心库和自有 native DLL。消费者桥接源帧所有权与执行作用域；扩展负责显示转换与像素查询。native 使用标准 CUDA 句柄，不创建第二套运行时，见 [CUDA 集成](cuda.md)。

Measurements/Hud 使用共享 Queries；Measurements、Rendering、Snapshots 使用 Imaging；这些模块均向 Frames 依赖，Frames 不依赖 Imaging。帧工厂组合存储和像素源，CPU 共用描述符和切片，GPU 接受生产者像素源。FrameStorage 仅管理引用及最终释放，不选择后端或独立释放像素源。

FrameLease 是后端无关读取边界，每次异步读取自持租约并验证请求/结果、提供来源。区域返回独立 CPU ImageFrame，RegionPixels 另存来源，不把源身份赋给派生像素。生产者 release 在最后源租约后清外部查询资源。Raw 快照接管区域，独立像素租约可越过快照。交付结果前先完成必要清理，无法交付时清理候选结果。

## 所有权与依赖方向

Viewer 发布门面和通知；Host 按单次提交回调、公共事件顺序连接，每个订阅边界隔离异常。Host 拥有 STA、窗口、管线、呈现、调度、测量集合、注册表、交互及 HUD。门面先赋 Host 再启动线程，初始化失败走同一幂等关闭流程，各所有者独立尝试安全清理。释放等待实际 STA 退出。

FramePipeline 计入所有已接纳提交直到释放和公开结果完成，包括提交线程上的替换清理。显示范围更新有独立版本结果，新请求/关闭取消等待。共享关闭结果保留资源/排空失败，通知失败隔离。D3D 附加无法证明安全分离时保留呈现器和表面租约至进程结束。

ViewerLifetime 统一停止门和关闭安全的句柄移除调度，HUD 集合拥有视觉并使句柄失效。ViewerInputBinding 把 WPF 来源解析为测量身份和图像坐标，负责效果和临时输入抑制，不保存会话状态。公共视口接收兄弟叠加上的导航和像素查询；ViewportPan 使用屏幕坐标，共享 MouseCaptureSession 保证捕获失败不开始拖动，取消/卸载/关闭结束捕获。

MeasurementInteractionCoordinator 唯一拥有工具、版本、模式、选中及转换策略，直接借用 MeasurementLayer。层可见和命中策略限制工具/编辑启动，重入取消后也检查；临时抑制不改配置。Registry 保存可复用注册，每次工厂生成一次性会话。激活聚合注册身份、创建上下文和会话；旧回调继续前验证身份，Finish 只作用所属上下文。所有预览通过所属上下文创建，结束后拒绝创建，每个会话终止恰好清理一次。

MeasurementCollection 拥有成员、视觉索引、创建策略与 Viewer 样式，Host 显式创建并共享 MeasurementRuntime。Runtime 统一 STA/生命周期调度、查询注册和通知，不依赖视觉容器。集合关闭先停 Runtime 再清对象。通知共用 FIFO，重入追加；内部几何投射同步先完成，公共通知后发。

Layers 容器拥有附加、变换、输入抑制及生命周期，不依赖具体绘图/测量类型；具体层依赖容器。ViewerLayers 组合消费者能力。DrawingLayer 统一单项和集合句柄；MeasurementLayer 通过清内容通知让集合清模型，层不反向拥有集合。清理门覆盖取消与资源释放全过程，finally 清残留视觉，不依赖 coordinator 存在。

MeasurementItem 拥有模型、查询订阅、呈现与释放；完成/释放清上下文引用，Origin 保留不可变来源。先解除查询/视觉，再发移除。Presentation 拥有 WPF、标签及可选曲线，关闭曲线请求删除对象，主动释放先断该回调。终止句柄释放集合/Runtime 引用，终止读只保留稳定数据和轻量生命周期门；绘图失效同样释放层引用。呈现关闭在 STA 清图像源，未知 native 附加独立隔离。

MeasurementQueryDefinition 统一几何/查询组合、构造与 ROI 裁剪，Client 独立缓存请求，每次保留几何版本和坐标。Geometry 是事实，GeometryEditing 定义控制点和拖动；LineProfileWindow 包装可选窗口。没有任意编辑器注册/动态工厂。EditController 拥有会话和控制点，先观察内部几何更新，程序更新重设拖动基线且保留控制点语义。

HUD 独立订阅共享 scheduler。查询协议/运行时是内部 Imaging 能力；一次同步发布同时携带身份和载荷，不分两个暂存通知。测试 Runtime 控制时间、工作和 UI 发布。LineSampling 独立于显示，曲线消费不可变结果并拥有缓冲，纯数据查询不创建曲线或 RGB 中间缓冲。

MenuManager 拥有注册和 WPF 绑定，句柄撤销禁用绑定并释放目标。普通菜单关闭保留绑定至输入排空，以支持 Closed-before-Click；新打开使旧绑定失效。执行可等待、错误隔离、同对象禁止并发。已开始工作自持资源可越过注册/Viewer。

ViewerMenuController 负责内置策略，借用交互、工具、层、HUD 和快照；Menus 下 MenuSnapshotSession 唯一拥有冻结帧/ROI，Snapshots 不拥有菜单策略。Host 在关闭管线前断菜单策略和绑定。Rendering 的 CommittedViewLease 原子固定帧、映射和版本，消费者不拥有管线状态。

## 像素格式归属

FramePixelFormats.GetInfo 是分量顺序、位深、数字表示、alpha 的唯一事实源，字节数、灰度和语义通道数由其派生。Frames/Pixels 解码及统计，Imaging 映射，Rendering 适配 WPF，Snapshots 编码布局/通道/标签，各自保留算法。显示范围不是原值限制，曲线通道不是格式通道，编码行长不必等于源 stride。新增格式必须逐一支持或拒绝解码、统计、显示、HUD/测量、快照，不能只加描述符。

## 扩展不变量

- 提交立即消耗所有权，任何终止路径公开结果及最终释放明确。
- CPU 缓冲与 native GPU 表面生命周期不同，ICpuImagePresenter 只抽象 CPU。
- 批量标记和逐项 WPF 测量服务不同负载，不混淆所有权和交互。
- Brush 跨 STA 前复制冻结，每个 Viewer 独立样式，已有视觉保留快照。
- 普通重入由最新会话接管；批量清理拒绝新会话、重入幂等、失败后继续其余层。
- 公共扩展暴露稳定消费者能力，不暴露控件/缓存/服务实例；API 基线与调用方及指南一起审查。

## 查询策略与工具接纳

查询策略独立选择接受旧几何及起始/完成间隔。完成指 STA 已处理结果，包含失败、过期和拒绝；订阅/会话/描述符检查始终保留。旧显示保留时间与新结果 MaxResultAge 独立。coordinator 统一激活接纳和重入检查，门面初始拒绝抛异常、菜单忽略不可用目标。操作固定注册实例，ID 撤销解析当前实例，注册关闭通过同一安全移除路径使引用失效。

## 帧组件协作

FramePipeline 管等待帧、当前帧、冻结代次、请求/已提交显示设置及结果；FramePresentation 管后台准备、STA 呈现和后端资源；MenuSnapshotSession 管菜单目标与保存互斥；SnapshotCapture 管串行区域读取和独立快照。

CommittedViewLease 原子捕获帧租约、显示范围和版本，不拥有导出同步。管线共享 ViewerLifetime 接纳门，昂贵上传在门外，附加图像和发布当前帧在门内；提交、冻结/恢复和呈现释放由 STA 串行。仅图像提交发帧通知，纯重绘不发。关闭先停管线和交互、释放菜单、测量与层，再等待渲染、查询及 STA。

## 交互实现与查询测试

`MeasurementInteractionCoordinator` 唯一拥有选中项、交互模式、工具及会话版本；注册表只保留注册。编辑控制器直接接收测量对象，持有编辑会话和控制点。命中测试通过集合解析已登记视觉，未登记的视觉不能参与编辑。

`ViewerInputBinding` 将 WPF 输入转换为图像坐标，并通过 `IInteractionView` 提供比例、指针效果和临时输入抑制，不复制交互状态。协调器借用测量层接收接纳策略与生命周期通知。测量集合独立订阅图层清理：先取消输入和选中状态，再尝试清理模型、查询和资源，即使没有协调器或取消失败，也不跳过清理。清理期间拒绝创建和新会话，重入清理幂等，一层失败后继续其他层。

`IQueryRuntime` 提供单调时钟、tick、工作执行和 UI 发布。测试手动推进这些边界，不依赖真实窗口或时间等待；生产实现使用 `DispatcherTimer`、`Stopwatch`、`Task.Run` 和 Dispatcher 发布。测量与像素 HUD 分别拥有自己的查询订阅，调度器撤销订阅而不释放订阅者。

## 线剖面渲染

横轴表示样本索引，纵轴表示像素值，仅显示数值刻度。主次虚线网格和柔和 RGB 曲线帮助区分数据；曲线使用圆角连接但不平滑样本。刻度随窗口尺寸调整，轴缓存直到尺寸、范围或 DPI 改变。冻结画笔与曲线几何由 WPF 复用，数据或尺寸变化时重建。

每个物理像素列按样本顺序保留有限段的端点及极值，非有限值形成间断，单样本显示为点。这仅限制显示细节，不改变原始样本。通道缓冲按几何级数增长并归曲线窗口所有；纯数据查询不创建 RGB 曲线缓冲。发布同步提供帧身份和只读样本，回调在返回前消费；数据变化时 WPF 序列化和帧查询仍会分配。

## API 基线

`tests/Fizzy.ImageViewer.Tests/PublicApi.txt` 记录经过审阅的导出类型、成员、可空性和默认值。API 测试在测试输出旁生成 `PublicApi.actual.txt`。先审查有意差异、更新调用方与契约，再接受基线；不能以更新基线代替契约审查。
