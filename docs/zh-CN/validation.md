# 验证

[English](../en/validation.md)

使用 Windows、PowerShell 7 和`global.json` 选择的 .NET 10 SDK，独立检出即可构建，不需要相机或机器人。库和测试同时面向 `net10.0-windows` 与 `net8.0-windows`；运行测试需要安装两个版本的 Desktop Runtime。共享库输出的命令串行执行。自动 WPF 测试使用隐藏窗口和注入边界，不证明物理显示、混合 DPI、远程桌面或真实 GPU 行为。

按任务要求、适用门禁及改变的行为选择下列检查。追加检查须对应具体问题，或从改动到被检查行为的调用、数据或资源依赖关系。下列场景用于选择覆盖范围，不是每次修改都要执行的清单。报告已执行的验证及结果，以及尚未完成的必需检查，不罗列无关的未验证场景。

## 构建与测试

```powershell
dotnet restore src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj --locked-mode
dotnet build src/Fizzy.ImageViewer/Fizzy.ImageViewer.csproj -c Release --no-restore
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
./scripts/Test-Samples.ps1
```

`./scripts/Test-Release.ps1` 是库、工具、示例、文档和包的完整门禁，不发布。依赖和浏览器安装见[文档维护](documentation.md)。发布前检查包 README、XML 和固定版本 URL；只有干净且 tag 匹配的 release-mode 验证能创建可发布 bundle。

## 像素查询

格式、来源或查询变更运行像素源、帧元数据、scheduler、HUD 和快照测试，覆盖独立租约、区域局部坐标、不可变结果、不支持能力、订阅/几何失效、有界接纳、公平、取消和操作/清理双重失败。TIFF 读取验证使用仅测试依赖 LibTIFF，不以写入器自证。

## 测量所有权与交互

会话/生命周期变更选择覆盖改变行为及其直接依赖的测量、交互和层测试。覆盖场景包括独立激活、工厂重入及失败、结束上下文、预览清理、恰好一次会话释放、通知顺序、程序编辑、控制点越界、注册撤销、关闭后外部租约。删除不再支持的恢复行为时保留线程/资源安全验证。

## 架构边界

公共 API 对照 PublicApi.txt 审查签名、可空性及默认值，不能盲目更新。联合工作区 API/行为变化还需父约定要求的消费者 Source 构建及测试；纯 XML 不改变签名，也不授权无关运行时修改。

初始化/关闭检查原错误、部分组合清理、共享终止失败、不重复破坏性释放、native 隔离和实际 STA 退出。菜单覆盖 Closed-before-Click、重新打开、撤销、独立动作资源和异步失败。

## 线剖面分配

改变热路径的分配、数据复制、排队、同步或调度行为时运行对应基准；仅改名、移动文件或修改注释不触发基准。合成结果不等于物理呈现或持续设备性能。

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --embedded artifacts/frame-benchmark-embedded.json
dotnet run --project tools/FrameBenchmark -c Release -- --snapshots artifacts/snapshot-copy-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --notifications artifacts/notification-benchmark.json
dotnet run --project tools/DrawingBenchmark -c Release
dotnet run --project tools/ResizeBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release -- --query-publication
```

通知模式在所属 STA 测量实际控件通知入口，包含独立窗口转发，覆盖 0/1/4 个外部订阅者；排除订阅设置、帧渲染和 Dispatcher 调度，报告预热后的单次通知耗时与线程分配。它还使用预建几何载荷测量共享测量通知队列的 0/1/4 订阅者场景，排除测量变化和呈现。使用同一机器和配置对比多轮结果。

帧/快照报告耗时和托管分配，不是进程峰值内存。绘制排除消费者结果构造、跨线程和物理呈现；线剖面区分数据、WPF 命令、离屏呈现和查询复制，不可变结果所有权必须保留。

## 工具会话与图层所有权

覆盖单项/集合/空内容替换、Visual 和点击身份、失败保留旧内容、整层清理、重入和句柄失效，确认清业务层不清 HUD。取消/释放失败不应终止替代会话；几何/查询保留半开像素覆盖、极大有限坐标及非有限样本断点验证。

## 人工检查

不默认启动示例或追加交互验收。自动化未覆盖的真实显示、DPI、输入及 GPU 行为，仅在任务或适用门禁明确要求时由人验收。在该范围内选择下列受影响场景；自动化覆盖缺口本身不构成追加验收的要求。代理不得将自动化操作报告为人工验收。

验收人运行对应[示例](getting-started.md)，记录系统、缩放、显示器布局、相关运行时/GPU 版本、commit、步骤和观察结果。按要求选择首次显示、叠加上滚轮/中键、工具启动/取消、选择/编辑/删除、层隐藏、曲线关闭、HUD、菜单导出或实时流中关窗。DPI 变化和远程桌面仅在要求且支持时纳入。真实 GPU 附加/切换及设备需要另外授权的环境。报告必需但未执行的场景，其他检查通过不能替代。

启动示例前确定正常关闭操作、有限的退出等待期限及失败清理方式。记录本次检查启动的各个进程，包括 PID 和启动时间，仅清理这些实例。强制结束仅作为清理证据，不证明正常关闭或资源排空通过。分别记录产品断言失败、环境/工具失败和未执行的检查。工具超时本身不能证明产品缺陷；证据不足时保留原因未明的结论。

文档变更按[文档维护](documentation.md)中对应变化类型执行门禁。发布/恢复还需[发布 SOP](releasing.md)、故障门禁及隔离服务演练；本地测试不授权正式发布。

## 绘制基准范围

持续负载测量每次分配、GC 次数、延迟分位数、预期标记/文本量与缩放。基准的 50 次热身后替换使用两组预建圆，排除结果创建、跨线程及物理呈现，不能证明真实持续流 GC 性能。

## 构建输出布局

应用和库输出统一位于本仓库 `build/Debug/` 或 `build/Release/`。独立库、工具按项目分目录，必要时继续按目标框架或 RID 隔离。测试程序及夹具位于 `build/<Configuration>/tests/<Project>/`，测试结果位于该项目的 `TestResults/` 子目录。中间文件及还原缓存位于 `build/obj/`，按项目和配置隔离。源码依赖使用各自仓库的输出根目录。

共享输出的构建串行执行。`artifacts/` 用于临时日志、报告、包暂存和发布校验。旧项目内 `bin/`、`obj/` 已排除在编译输入之外，仅在确认不再使用后清理。
