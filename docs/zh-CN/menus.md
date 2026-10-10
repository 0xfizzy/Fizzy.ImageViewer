# 菜单

[English](../menus.md)

实现 `IMenuItem`，保留 `RegisterMenuItem` 返回的释放句柄。Viewer 借用菜单对象；释放注册只撤销注册，不释放对象。句柄支持任意线程重复释放，包括 Viewer 关闭后。

## 打开与执行

每次打开时在 STA 读取 `Header`、`IsVisible` 和勾选状态。读取失败取消此次打开并记录日志。`ExecuteAsync` 在 STA 开始，应返回实际工作，不启动无人观察的任务。执行异常被记录并隔离，同一菜单对象不能跨多次打开并发执行。

WPF Closed 可能早于 Click，因此普通关闭保留绑定直到输入排空。重新打开或撤销注册会使旧绑定失效。已开始工作可越过菜单和 Viewer 生命周期，必须自行拥有依赖。

## 固定目标

内置冻结和导出保留该次打开时捕获的已提交帧、映射和适用 ROI。独立租约允许已接纳工作在关闭后结束，导出不会改用新帧。旧取消动作不能取消替代的测量会话。

自定义异步动作必须明确选择并拥有目标。借用 Viewer 不意味着关闭后仍可任意调用。参见[快照](snapshots.md)和[注册示例](public-api.md#menu-registration-lifetime)。
