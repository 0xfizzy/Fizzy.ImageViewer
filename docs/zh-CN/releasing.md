# 发布 SOP

[English](../releasing.md)

适用于 v1.2.0 及以后，不补档旧版。NuGet 有效载荷、不可变 `/v{version}/r0/` 文档、版本索引和 GitHub Release 一致才算完成。禁止移动已发布 tag 或覆盖历史版本。NuGet 和 Pages 是独立服务，部分发布可以恢复，但不是原子事务。

## 一次性配置

1. GitHub Settings → Pages 选择 **GitHub Actions**。`github-pages` environment 允许 main 和发布 tag；发布任务在 tag 上执行。审批按仓库策略设置。
2. NuGet Trusted Publishing 配置 owner `0xfizzy`、仓库 `Fizzy.ImageViewer`、工作流 `release.yml`；策略需要环境时设为 `github-pages`。仓库变量 `NUGET_USER` 保存用户名，不保存长期 API key。
3. 发布任务需要 `contents: write`、`pages: write`、`id-token: write`；PR 和巡检只读。保护 main、发布 tag 和 `docs-archive`，允许发布机器人追加归档提交，禁止强推和删除。环境 tag 策略必须匹配工作流。
4. 获得授权的维护者执行 `./scripts/Initialize-DocumentationArchive.ps1` 创建空归档分支。此命令会推送新分支；已有分支时拒绝覆盖。
5. 核对 `docs/site.json`。隔离演练仓库需修改其配置、工作流和信任设置；不得把演练包发往正式 NuGet ID。先运行离线故障门禁。
6. 另一位维护者仅按本 SOP 在隔离环境完成正常发布及包成功后部署失败的恢复。在长期保留的发布就绪 issue 中记录 commit、workflow 链接、结果和未完成的人工检查。本地故障夹具不等于真实 OIDC/Pages 演练。

## 准备发布

在同一审阅 PR 更新项目版本、发布说明、受影响 XML/英文指南、中文及示例。版本使用不含构建元数据的 SemVer：项目 `1.2.0` 对应 tag 和目录 `v1.2.0`；`1.3.0-rc.1` 不替换稳定版入口。

```powershell
npm ci
npx playwright install chromium
./scripts/Test-Release.ps1 -Version 1.2.0
node scripts/Test-Browser.mjs
./scripts/Serve-Docs.ps1
```

审查 `artifacts/release/validation.json`、日志、测试结果、包内容及双语页面。本地准备不要求 tag 或干净检出，也不能生成可发布 bundle。另行记录交互、基准和消费者验证；构建不证明物理显示、混合 DPI 或设备行为。

检查通过后合并 PR。维护者明确创建并推送 reviewed main commit 上的 tag 即授权发布：

```powershell
git status --short
git rev-parse HEAD
git tag -a v1.2.0 -m 'release v1.2.0'
git push origin v1.2.0
```

先确认 HEAD 是审阅后的 commit 且检出干净。工作流再次核对项目版本、tag 身份和 `origin/main` 可达性。

## 自动阶段与成功判据

1. **准备**：锁定还原、库测试、工具/示例构建、严格文档、故障门禁、浏览器及包检查。只打包一次，从该原始包提取 DLL/XML 重新生成正式 API 文档；`New-ReleaseBundle` 封存哈希和身份。
2. **归档**：创建 draft Release，上传单个 `release-bundle.zip`，内含原始 nupkg、文档 zip、清单及验证报告。发布 NuGet 前下载核验。Actions 产物只是中转。
3. **NuGet**：通过 OIDC 获取短期 key，发布或下载核验已有版本，不使用 `--skip-duplicate`。比较除服务端新增签名以外的所有有效载荷条目。
4. **固定文档**：恢复归档站点，合并到 `docs-archive/v{version}/r0/`，不强推；部署全部历史站点并验证每个线上文件。未完成版本不成为 latest。
5. **完成**：标记完成，root/latest 指向最高已完成稳定版，再部署检查；最后才发布 GitHub Release。

最后一步必须显示 `PASS` 和固定文档地址。核查 Release 已非草稿、NuGet 版本、双语导航、API/搜索和版本选择。原始 bundle 永久保留。

归档写入及 Pages 部署与 dev 共用并发组。GitHub 可能替换等待任务；取消不代表完成。每日/手动巡检识别缺失、草稿或身份不一致的发布。

## 故障恢复

优先从 main 手动触发工作流，指定原 tag：

```powershell
gh workflow run release.yml --ref main -f tag=v1.2.0
gh run list --workflow release.yml --limit 5
```

手动恢复使用 main 上已审阅的工作流和脚本，包及源码身份仍为原 tag。准备任务需要 `contents: write` 才能看到草稿 Release 附件；PR 验证仍只读。已有 bundle 时下载原件，不重新构建。NuGet 已有该版本但归档不可见时，准备任务停止。获得 Release 附件访问权限后，本地诊断：

```powershell
./scripts/Resume-Release.ps1 -Tag v1.2.0
./scripts/Test-PublishedRelease.ps1 -Bundle artifacts/bundle -RequireCompleted
./scripts/Test-ReleaseStatus.ps1
```

`Resume-Release` 仅恢复和核验；工作流以相应权限继续发布部署。重复成功执行不改变固定内容或使 latest 回退。身份冲突始终停止。

| 故障 | 操作与预期结果 |
| --- | --- |
| 归档前验证失败 | 尚未发包；修复并审阅新的未使用发布身份，不自动移动旧 tag。 |
| 归档上传中断 | 检查单个 bundle 资产；已有则恢复，没有则重跑原成功准备产物。同名不同内容拒绝。 |
| NuGet 请求超时 | 先查询下载；匹配则继续，尚未出现或索引中则保持未完成并重试。 |
| NuGet 同版内容不同 | 停止调查并选择新版本，不能删除覆盖以强行成功。 |
| NuGet 成功、Pages 失败 | 保留草稿和原件，触发同一 tag，不重新打包。 |
| 线上文件或身份不同 | 不标完成，核查归档 commit、部署/CDN 后重试。 |
| latest/索引部署失败 | 固定版本保留，同 tag 恢复部署已验证索引。 |
| GitHub 完成响应丢失 | 查询实际 draft 状态后重跑。 |
| Actions 产物过期 | 从永久 Release bundle 和归档分支恢复。 |
| 发包后原 bundle 缺失 | 停止调查，不能重建并冒充原产物。 |

网络重试有上限，索引/CDN 延迟会明确失败，不假定成功。巡检只读，workflow 日志及 GitHub Actions 失败通知是故障入口。tag 内脚本存在不可恢复缺陷时，用审阅后的工作流修复处理保留 bundle，不改 tag 或历史文档。

Pages 以文档归档提交标识部署版本，而非包的源码提交。开发站点、固定版本暂存和索引激活可能共享源码，但站点内容不同。产物名称包含阶段、运行和尝试编号，避免重试重名。部署 API 报告成功后仍须核验线上内容。

## 可选 CUDA 包

主包可用后，从同一源码 tag 独立发布 `Fizzy.ImageViewer.Cuda`。在满足 [CUDA 构建前提](cuda.md#构建与部署)的 Windows 主机上，从 tag 导出的源码准备产物，编译原生测试但不运行硬件，执行托管 CUDA 测试并封存原始包：

```powershell
./scripts/New-CudaReleaseBundle.ps1 -Tag v1.2.0
./scripts/Publish-CudaRelease.ps1 -Stage Archive -Tag v1.2.0 -Bundle artifacts/cuda-release-preparation/bundle
gh workflow run release.yml --ref main -f tag=v1.2.0 -f component=cuda
```

Archive 前审查准备日志、测试结果和 bundle 验证报告。独立且不可变的 `cuda-release-bundle.zip` 附件包含包、原生文件哈希、源码身份及验证报告，不替换主包归档或固定文档。工作流下载核验附件，通过同一 NuGet Trusted Publishing 策略发布，再比对下载的 NuGet 有效载荷；策略必须允许 CUDA 包 ID。恢复时重复同一 dispatch，不重新构建已发布版本。硬件、驱动和物理显示验证独立于托管测试及原生编译。

## 不发包的文档修订

修订要求原发布已完成并归档 r0；旧固定目录保留，不原地转换。Prepare 要求干净检出，在当前目录从活动修订源码创建并切换新分支，保留之前的修正，并建议下一个修订号；可用 `-Branch` 指定新分支名。所有修改仍对照原始 tag 检查。

```powershell
./scripts/Revision-Docs.ps1 -Action Prepare -Tag v1.2.0
# 在准备好的分支中修改指南/翻译、导航、示例 C# 或 XML 注释；
# 审阅翻译、接受哈希并提交。不改运行时代码、构建配置或工具。
./scripts/Revision-Docs.ps1 -Action Check -Tag v1.2.0 -Revision 1
git push -u origin docs/v1.2.0-r1
./scripts/Revision-Docs.ps1 -Action Start -Tag v1.2.0 -Revision 1 -Branch docs/v1.2.0-r1
./scripts/Revision-Docs.ps1 -Action Status -Tag v1.2.0
./scripts/Revision-Docs.ps1 -Action Resume -Tag v1.2.0 -Revision 1
./scripts/Revision-Docs.ps1 -Action Rollback -Tag v1.2.0 -Revision 0
```

Check 要求已提交的干净源树，拒绝非注释运行时代码变化，使用已发布原始 DLL 和审阅后的 XML。示例针对原始包编译；严格 XML、翻译、站点和浏览器检查必须通过。工作流先检查允许修改范围，再执行候选脚本。Start 将单个 `documentation-rN.zip` 保存到原 Release，下载核对字节后才部署。共用部署锁内再次检查同名内容冲突；禁止覆盖附件，不同内容选择新修订。

先部署固定修订并核验每个文件，再激活入口；root/latest 只选择已完成稳定版。Resume 下载原修订产物，不重新构建。Rollback 验证目标已完成修订，只移动活动指针。旧完成发布重试不能撤销后续修订或主动回滚。两个阶段均检查线上入口，失败在 Actions 中明确显示。运行 `./scripts/Test-DocumentationRevision.ps1` 离线验证不可变、激活、回滚、冲突和源修改限制。任何修订操作都不发布 NuGet 包。

仅预览开发文档时，初始化归档后在 main 触发 `docs.yml`；不需要 NuGet 凭据或发布 tag。没有稳定版时首页链接到 `/dev/`。
