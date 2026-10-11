# 文档构建与维护

[English](../documentation.md)

英文 XML 定义符号契约，英文指南定义跨 API 协议，中文指南同步维护。不要为使注释检查通过而改变运行时行为。审查所有权、返回含义、取消、线程与清理时，对照实现和测试；编译通过不等于语义覆盖完整。

## 构建与预览

DocFX 配置位于 `docs/docfx.json`，站点首页与顶层导航位于 `docs/landing/`。`Build-Docs` 将输入暂存到 `artifacts/docs/source/`，在其中运行此配置，保持发布 URL 不变。

安装 PowerShell 7、`global.json` 指定的 SDK、.NET 8 Desktop Runtime、Node.js 22.14.0 和 Git。库仍目标 .NET 8，固定 SDK 的编译器支持现有测试源码。仓库固定 DocFX 和 Playwright。在根目录运行：

```powershell
npm ci
npx playwright install chromium
./scripts/Build-Docs.ps1
node scripts/Test-Browser.mjs
./scripts/Serve-Docs.ps1
```

`Build-Docs` 验证翻译、锁定还原依赖、严格重编译 XML、从实际 DLL/XML 提取 API、构建站点、检查 HTML 链接并运行浏览器冒烟。不运行 WPF 示例、设备或发布部署。API 输入、清单和站点位于 `artifacts/docs/`。预览使用项目路径及 `/dev/` 或 `/v{version}/`，打开打印的 HTTP 地址，不使用文件 URL；Ctrl+C 停止。

`Test-Browser` 用固定 Chromium 和本地服务器检查双语入口、一个 API 页面、语言切换、搜索结果跳转、版本切换、缺页回退和页面错误。仅失败时截图，结果报告及失败截图位于 `artifacts/docs/browser/`。模拟版本挂载不表示实际发布已上线。部署后运行 `node scripts/Test-Browser.mjs --url https://0xfizzy.github.io/Fizzy.ImageViewer/dev/` 检查线上行为；版本缺页回退只使用本地夹具。

公共 XML/声明、指南/翻译、README、导航、模板或工具变更必须完整构建文档并运行浏览器冒烟。CI 对每次推送和 PR 执行完整门禁。保持契约的内部修改无需无关指南编辑。共享库输出的构建必须串行。

## 翻译审阅

英文指南位于 `docs/`，中文同名文件位于 `docs/zh-CN/`。保持对应链接、API 标识和示例源码一致。检查器要求双向语言链接及两份导航入口，并拒绝孤立译文。NuGet README 是英文打包模板，不属于指南翻译对。

人工核对最终英文和中文后执行：

```powershell
./scripts/Test-Documentation.ps1 -AcceptTranslation docs/snapshots.md
./scripts/Test-Documentation.ps1
```

`docs/translations.json` 保存规范化源哈希，纯换行变化不使审阅失效。禁止未审译文就接受哈希；接受过程中仍检查链接。完成前检查双语渲染页面。

## 示例与门禁

```powershell
./scripts/Test-Samples.ps1
./scripts/Test-DocumentationGate.ps1
./scripts/Test-ReleaseGate.ps1
pwsh -NoProfile -File scripts/Test-ReleaseRecovery.ps1
./scripts/Test-Workflows.ps1
```

示例构建不打开窗口；交互检查手工运行[示例](getting-started.md)。文档故障夹具验证缺失 XML、过期或缺失翻译和坏链接；发布夹具验证同版本覆盖、过期候选、稳定版排序、包有效载荷比较和恢复。恢复测试在独立进程中用假 GitHub/NuGet/Pages 边界执行实际编排，覆盖 NuGet 响应丢失和 Pages 失败，报告位于 `artifacts/recovery-fixture/result.json` 并明确标为模拟。工作流检查使用固定校验和的 actionlint 1.7.7。测试使用可清理的 `artifacts/`，不发布真实版本。

依赖变更应主动进行非锁定还原或 npm install，审查锁文件差异，再执行锁定验证；发版时禁止重新生成锁文件。生成 HTML、API 元数据和本地报告不提交源码仓库。

## 版本发布

`docs/site.json` 集中配置仓库、域名、项目路径、最低留存版本和归档分支。固定文档使用 `/v{version}/`，更新整体覆盖该目录；主分支使用 `/dev/`。每个版本保留独立搜索、资源、示例及源码身份。Git 历史保留旧内容供回滚，不再公开修订路径或修订选择。

归档分支保留生成站点，GitHub Release 资产独立保存原始发布包，不依赖 Actions 保留期。不得编辑已归档版本。首次配置、发布、部分失败和恢复见[发布 SOP](releasing.md)。构建不授权发布。

`Serve-Docs.ps1 -SiteDirectory artifacts/docs/site -Port 8087` 仅按真实项目/版本前缀服务已有产物，不构建、不访问网络。开发部署取得共用部署锁后核对源 SHA，拒绝不再对应最新 main 的构建。

每个新构建的快照包含记录页面和锚点的 `pages.json`。版本切换使用该清单，缺页时回退到对应的 API 或语言首页，仅保留存在的锚点。若旧快照没有清单或请求失败，选择器恢复当前版本，并在悬浮提示中显示错误；需要时可直接打开目标版本。历史快照不改写。
