# 验证

[English](../validation.md)

使用 Windows、PowerShell 7 和固定 SDK，独立检出即可构建，不需要相机或机器人。共享库输出的命令串行执行。自动 WPF 测试使用隐藏窗口和注入边界，不证明物理显示、混合 DPI、远程桌面或真实 GPU 行为。

## 构建与测试

```powershell
dotnet restore Fizzy.ImageViewer.csproj --locked-mode
dotnet build Fizzy.ImageViewer.csproj -c Release --no-restore
dotnet test tests/Fizzy.ImageViewer.Tests/Fizzy.ImageViewer.Tests.csproj -c Release
./scripts/Test-Samples.ps1
```

`./scripts/Test-Release.ps1` 是库、工具、示例、文档和包的完整门禁，不发布。依赖和浏览器安装见[文档维护](documentation.md)。发布前检查包 README、XML 和固定版本 URL；只有干净且 tag 匹配的 release-mode 验证能创建可发布 bundle。

<a id="pixel-queries"></a>

## 像素查询

格式、来源或查询变更运行像素源、帧元数据、scheduler、HUD 和快照测试，覆盖独立租约、区域局部坐标、不可变结果、不支持能力、订阅/几何失效、有界接纳、公平、取消和操作/清理双重失败。TIFF 读取验证使用仅测试依赖 LibTIFF，不以写入器自证。

<a id="measurement-ownership-and-interaction"></a>

## 测量所有权与交互

会话/生命周期变更运行测量、交互和层测试，覆盖独立激活、工厂重入及失败、结束上下文、预览清理、恰好一次会话释放、通知顺序、程序编辑、控制点越界、注册撤销、关闭后外部租约。删除不再支持的恢复行为时保留线程/资源安全验证。

<a id="architectural-boundaries"></a>

## 架构边界

公共 API 对照 PublicApi.txt 审查签名、可空性及默认值，不能盲目更新。联合工作区 API/行为变化还需父约定要求的消费者 Source 构建及测试；纯 XML 不改变签名，也不授权无关运行时修改。

初始化/关闭检查原错误、部分组合清理、共享终止失败、不重复破坏性释放、native 隔离和实际 STA 退出。菜单覆盖 Closed-before-Click、重新打开、撤销、独立动作资源和异步失败。

<a id="line-profile-allocations"></a>

## 线剖面分配

热路径变化运行相关基准；合成结果不等于物理呈现或持续设备性能。

```powershell
dotnet run --project tools/FrameBenchmark -c Release -- artifacts/frame-benchmark.json
dotnet run --project tools/FrameBenchmark -c Release -- --snapshots artifacts/snapshot-copy-benchmark.json
dotnet run --project tools/DrawingBenchmark -c Release
dotnet run --project tools/ResizeBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release
dotnet run --project tools/LineProfileBenchmark -c Release -- --query-publication
```

帧/快照报告耗时和托管分配，不是进程峰值内存。绘制排除消费者结果构造、跨线程和物理呈现；线剖面区分数据、WPF 命令、离屏呈现和查询复制，不可变结果所有权必须保留。

<a id="tool-sessions-and-layer-ownership"></a>

## 工具会话与图层所有权

覆盖单项/集合/空内容替换、Visual 和点击身份、失败保留旧内容、整层清理、重入和句柄失效，确认清业务层不清 HUD。取消/释放失败不应终止替代会话；几何/查询保留半开像素覆盖、极大有限坐标及非有限样本断点验证。

## 人工检查

运行三个[示例](getting-started.md)，记录系统、缩放、显示器布局和 commit。检查首次显示、叠加上滚轮/中键、工具启动/取消、选择/编辑/删除、层隐藏、曲线关闭、HUD、菜单导出、实时流中关窗。按支持范围重复 DPI 变化和远程桌面。真实 GPU 附加/切换及设备需要另外授权的环境，明确报告未执行场景。

检查双语页面、搜索、项目路径下嵌套 API 和版本选择。发布/恢复还需[发布 SOP](releasing.md)、故障门禁及隔离服务演练；本地测试不授权正式发布。
