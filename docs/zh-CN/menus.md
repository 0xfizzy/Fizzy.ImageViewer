# 菜单

[English](../en/menus.md)

向右键菜单添加应用动作。功能存续期间保留注册句柄；Viewer 借用菜单对象，调用方拥有对象本身。

## 注册动作

以下示例假定已有存活的 `viewer`：

```csharp
using var registration = viewer.RegisterMenuItem(
    new Fizzy.ImageViewer.Menus.MenuItem("Mark ready", () => viewer.HudLabelText = "Ready"));
```

可实现 `IMenuItem`，或使用接受 `Action` / `Func<ValueTask>` 的辅助类。异步动作应返回其工作，不启动无人观察的任务，这样框架才能等待完成并记录失败。

## 可见性与目标

每次菜单打开都会在 Viewer STA 读取 `Header`、`IsVisible` 和选中状态。动作辅助类接受可选可见性谓词；`SeparatorMenuItem` 表示分隔符，渲染器删除开头、重复和末尾分隔符。

`MenuItem` 和 `CheckableMenuItem` 要求非空且非全空白的标题，以及非 null 动作；可选中项还要求状态 getter。构造时拒绝非法参数，有效标题按原文保留。内置交互动作固定打开菜单时的目标，因此旧“取消测量”动作不会取消替代会话，即使工具相同。

## 注册与执行生命周期

`RegisterMenuItem(IMenuItem)` 返回独立的 `IDisposable` 句柄。任意线程均可幂等释放它，Viewer 关闭后也安全。同一菜单对象可注册多次，各注册独立；忽略句柄则注册持续到 Viewer 关闭。Viewer 不释放调用方拥有的菜单对象。

撤销会禁用已打开菜单的对应项并阻止新回调，下次打开时不再显示。已开始的动作可继续完成，包括动作撤销自身或 Viewer 关闭的情况。`ExecuteAsync()` 返回 `ValueTask`，从 Viewer STA 启动，不传递 WPF 事件对象；绑定等待完成并隔离记录异常。同一个菜单对象的动作不能跨菜单打开并发执行，其他动作不受影响。

可见性和选中状态回调可修改注册；菜单构建期间新增的注册下次打开才出现。不要在这些回调中等待正在调用 Viewer 的其他线程。超过 Viewer 生命周期的异步工作必须自己拥有依赖，不再依赖已关闭的 Dispatcher。

WPF 的 Closed 可能先于 Click，普通关闭会保留绑定直到输入排空。重新打开、显式撤销和 Viewer 关闭会使旧绑定失效。如果构建菜单在冻结帧后失败，Viewer 恢复提交、删除部分绑定并结束本次打开。标题、可见性和选中状态读取失败在 WPF 事件边界记录，不逃逸到 Dispatcher；下次打开重新读取。Viewer 关闭会释放注册、回调和菜单绑定。

## 捕获图像目标

内置冻结与导出动作保留本次打开时的已提交帧、映射及适用 ROI。独立租约允许已接纳工作在窗口关闭后完成，导出不会换成较新的帧。自定义异步动作必须明确选择并拥有目标；借用 Viewer 不承诺关闭后仍可任意访问。详见[快照](snapshots.md)。
