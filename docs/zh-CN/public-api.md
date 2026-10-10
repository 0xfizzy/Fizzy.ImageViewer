# 公共 API 与 Viewer 生命周期

[English](../public-api.md)

`Viewer` 创建独立 STA 窗口，`IViewer` 是完整消费者契约；Viewer 为 sealed，应用适配器通过组合拥有它，不访问原始窗口。生成式 API 参考列出类型和成员，XML 定义符号契约，组件指南定义协作协议。

<a id="borrowing-viewer-capabilities"></a>

## 借用能力接口

拥有窗口生命周期的组件使用 `IViewer` 并等待异步释放。只使用部分能力的组件借用帧输入、已提交帧读取、窗口、显示、绘制、HUD、测量、菜单、查询或快照接口。接口由同一个 Viewer 实现，并不建立独立生命周期或安全隔离。

全局图层管理只在 `IViewer.Layers` 上。绘制接口只暴露默认 Markers，测量接口暴露 Measurements。图层句柄允许管理整层，包括清空他人内容；只需更新单项时传递单项句柄。使用能力接口不会让调用方获得私有实现、线程或调度器所有权。

<a id="threads-and-window-lifetime"></a>

## 线程与窗口生命周期

`showWindow: false` 支持显示前配置、订阅和提交。`Show` 显示、恢复最小化并请求激活；`Hide` 隐藏但保留内容；`Minimize` 不隐式显示已隐藏窗口。已显示但最小化的窗口仍为可见，最小化状态独立查询。普通属性和窗口操作在关闭开始后抛出 `ObjectDisposedException`。

Viewer 只对应一次窗口生命周期，关闭后不能重新打开。无论是否显示过，都需 `await DisposeAsync()`；它关闭窗口并排空已接纳工作，等待真实 STA 退出。重复调用观察同一成功或失败结果，不重复破坏性清理。安全清理后传播资源清理错误，通知订阅者错误被日志隔离；不能证明安全分离的 native 资源继续隔离保留。

构造前验证窗口边界。初始化失败在所属线程清理并排空后台工作，重新抛出原始异常；清理同时失败时保留两个错误。应用适配器将用户关闭和显式释放汇入一个幂等清理流程；如果转发测量删除通知，保留订阅直到清理结束。

`Closed` 只是窗口通知，不能替代等待释放。回调在 Viewer STA，不得同步等待关闭或其他依赖该 Dispatcher 的工作。关闭后的帧提交仍消耗帧并返回 `Closed`；绘图和 HUD 句柄关闭后仍可幂等释放。

`HudLabelText` 控制右上标签，`IsPixelInfoEnabled` 默认开启，控制左下像素查询。`EndInteraction()` 结束创建或编辑，保留已完成测量和已应用编辑。

`FitToViewport` 可产生超出普通滚轮 0.01–100 范围的缩放；此时滚轮只允许朝该范围方向变化。使用 `SetDisplayRangeAsync` 观察重绘失败。属性读取最新请求，快照使用最后成功提交的映射；较新请求或关闭取消等待中的更新。无帧时只配置后续输入，立即完成。详见[帧](frame-pipeline.md)。

<a id="measurement-notifications-and-extensions"></a>

## 测量通知与扩展

只对完成项发出完成、变更和删除通知，预览不发出。快照不可变并含 ID、几何、来源及版本，实时句柄可能已反映后续变化；回调在 STA，订阅者失败被隔离。完成不代表查询结果就绪。重入通知排队保持顺序，删除后不再变更，`Closed` 晚于删除通知。

工具每次激活创建独立会话；通过上下文创建预览，Complete 保留结果，Finish 只结束所属激活。查询种类和几何组合在附加前验证；曲线窗口只能配合线剖面查询，数据查询可不建窗口。详细协议见[测量](measurements.md)。

<a id="tool-registration-lifetime"></a>

## 工具注册生命周期

注册句柄仅撤销其所属注册实例，不删除已完成测量。ID 撤销作用于当前同名注册。注册元数据在 STA 读取一次；关闭和替换使旧句柄失效。替换、重入和清理规则见[工具所有权](measurements.md#tool-registration-ownership)。

<a id="internal-implementation"></a>

## 内部实现

内部 Host、渲染、查询调度器、测量显示层和 WPF 控件不构成公共扩展接口。测试可观察内部对象，但消费者应依赖公开门面、句柄、几何及工具会话。详见[架构](architecture.md)。

<a id="menu-registration-lifetime"></a>

## 菜单注册生命周期

注册返回独立释放句柄，菜单对象仍归调用方。撤销使旧绑定失效；已经开始的异步动作可在关闭后继续，必须自行拥有依赖。菜单状态和动作启动发生在 STA，异步执行失败被记录隔离。完整规则见[菜单](menus.md)。

<a id="layer-capabilities"></a>

## 图层能力

Markers 和自定义绘图层支持单元素或集合 `Add`，返回支持相同两种 `Replace` 的 `DrawingHandle`；替换整个内容，不改变身份和叠放顺序。点击标识整项绘图。测量层由工具创建模型内容，没有批量绘图接口。

两种层共享名称、可见、命中测试、ZIndex、Clear。Items 返回成员快照，ClearContents 保留图层；内置层不可移除，消费者不可派生层基类。

<a id="api-baseline"></a>

## API 基线

`tests/Fizzy.ImageViewer.Tests/PublicApi.txt` 记录导出类型、成员、可空性及默认值。测试输出 `PublicApi.actual.txt`；先审查差异并更新调用方、XML 和指南，再接受基线，不能以更新基线代替契约审查。
