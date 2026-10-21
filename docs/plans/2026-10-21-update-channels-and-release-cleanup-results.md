# 更新通道、发行精简与 Live 声纹实施报告

> 后续状态（2026-10-21）：实现已合并并推送 main（`3ce291c`），随后全部本地工作进入 dev（`4f338c8`），源码标签 `v0.10.0-dev.1` 已创建。用户现进一步授权：dev CI 完成后 PR 到 main，以 `0.10.0` 创建正式标签并发布 Release。下文“未合并、未推送、暂不发布”等表述记录历史阶段，不代表需要重复实施或撤销本轮发布授权；正式产物必须由最终 main 提交重建并验收。

日期：2026-10-21。原计划：`2026-10-21-update-channels-and-release-cleanup-plan.md`（含追加 Live 需求）。

## 0.10.0-dev.1 本地复验与源码标签

按后续用户指令，全部本地工作纳入 dev，采用规范预览版本 `0.10.0-dev.1`（数字程序集 `0.10.0.0`），标签 `v0.10.0-dev.1`，暂不发布 Release。原有 E包导出图片和 README 截图工具保留并纳入提交，历史规划去除纯空白差异并补齐当前状态；忽略目录中的备份、工具链和验证产物不提交。

实际预览构建发现并修复三项版本联动遗漏：Avalonia 回归使用统一版本解析；首次引导识别 dev 版本并保留已触发系列；咕嘎发行附件名跟随完整应用版本，包内素材版本和原始内容保持不变。新增 Windows/共享核心的首次预览、跨系列升级、同系列转正式不重复引导及非法预览号检查，保留修复前的失败证据。

本次本机验证记录位于仓库根目录 `work/dev-0.10.0-dev.1/`：Windows 124 个目标最终全部有通过证据；首轮 123 个通过，BundledSkinTests 触及自定义外层 150 秒上限，其他负载结束后同一二进制、同一上限单独复测 51.1 秒通过全部 66 项，未放宽断言。6 组 Python 回归通过；共享核心 393 项检查、Avalonia 2054 项断言通过；首次引导 Windows 53 项通过；ReleaseUiSmoke 29 项及即时更新回归通过。完整预览 PackagingSmoke 通过实际干净源码编译、18 文件运行清单、五个角色包、独立模板、源码和摘要校验、真实助手应用 ZIP 及真实 Inno 安装器编译。截图工具实际生成 1440×960 离线会话截图，SVG/PowerShell/Python 语法及差异空白检查通过。

早期受限环境下的临时目录访问错误和终端子进程探针失败已保留日志；切换正常本机权限、使用 work 内临时目录后通过。原生 macOS、真实 Live 通话、公网 Release 更新链仍沿用下文的未验证边界。本次不运行发布 workflow，也不替换现有 Release 附件。

## 交接状态

- 仓库： https://github.com/Eason4869/deskpet 。已核实当前最新正式 Release 是 v0.9.3，发布时间 2026-10-20 04:26:21 UTC（北京时间 12:26:21）。0.9.2 仅作为额外历史兼容样本。
- 实施目录：`E:\workspace\codex\deskpet\work\update-channels-cleanup`；分支：`codex/update-channels-cleanup`；基线：`318a82bf754b349445a08ed0bfc3bc3893e1dbe0`（实施时 origin/main，显示版本 0.9.3）。
- 原目录 `E:\workspace\codex\deskpet` 的 main 和全部既有未提交改动保留，未重置、覆盖或合并。实施内容保存在隔离分支，本地提交可由该目录的 `git log -1` 查得。
- 未修改发布版本号；未推送分支、创建远端标签、运行发布 workflow 或发布 Release。0.9.4、0.9.4-dev.2 仅用于隔离源码副本中的验证，不是已发布版本。
- 代码、文档和本地验证已完成。后续发布须取得用户新指令；不能把验证用的 0.9.3 包替换到现有 Release。

## 已实现行为

1. Windows 关于页提供正式版（main）和预览版（dev）选择。正式只接受正式 Release；预览接受正式与预览中的最高语义版本。选择即保存，不触发下载；更新忙碌期间不能改通道。
2. 共享版本比较支持数字三/四段、旧 dev 与 dev.N；新发标签只允许 vX.Y.Z 或 vX.Y.Z-dev.N。同底版 dev.2 < dev.10 < 正式版。切回 Stable 时若正式版较旧，保持当前版本并提示，没有降级入口。
3. 查询分页发行列表，过滤草稿、标签/预发布标志不一致和错误平台/模式附件；最高版本附件不完整时明确报错，不静默退选旧版。要求大小、SHA-256、仓库/标签 URL，并解析轻量或附注标签到 commit。
4. 应用和 helper 写入完整版本+40位 commit；更新任务协议 2 校验当前安装、helper 来源/自身和目标 PE 身份，处理相同数字程序集版本的 dev→正式更新。保留纯数字旧任务协议供正式桥接。
5. “立即更新并重启”跳过剩余等待；手动检查和自动更新仍在安全空闲后等 5 秒。立即操作仍保留任务、隐藏草稿、历史/设置保存、全屏和对话/Live 等等待条件，复用现有退出与 helper 交接流程。
6. Inno 在覆盖前验证 EXPECTEDBUILD，阻止手动降级，允许同版修复。新 helper 在安装失败时恢复旧受管文件及 HKCU 卸载记录，保存备份和 error.log。旧 helper/历史安装器无法补上新保护，不能宣传其有相同保障。
7. ZIP 与 Inno 共用精确清单/全新 staging/PE 验证：4 个程序及配置文件、运行 README、CHANGELOG、12 份用户教程。去掉运行包内创作模板、宣传图和开发文档。源码仍含模板、验证脚本和本计划。独立模板 ZIP 含 high-fps-v2 与 legacy-v1；设置改为“下载制作模板”。教程未随包的引用转为当前版本仓库链接。
8. 升级仅清理固定路径和哈希都匹配的旧发行文件；93 条规则逐项匹配真实 v0.9.3 ZIP，无不匹配项。保留修改/新增文件、data、导入外观和重解析点，失败恢复已删除内容。
9. Live 声纹使用现有 40ms 刷新和单调时钟产生连续形变，叠加真实音量；稳定有声时仍波动，静音后回落。PCM 音量从整段平均改为按实际播放的 200ms 分块计算，不新增永久计时器/音频线程。
10. 新 Windows release workflow 默认 publish=false；固定干净标签和分支归属，校验完整附件/远端摘要后才可显式公开。共享发布队列串行处理，latest 查询失败即停止，正式 latest 必须递增，dev 永不置 latest。Mac 数字 bundle 版本与完整显示版本分离，保留既有签名策略。

## 静态审查与源码清理

| 旧内容 | 处理与边界 |
| --- | --- |
| ConfigureCommands / ListCommands / RunCommand / CommandsEnabled 等 recipe 运行链 | 删除专属解析、执行、schema 与中间字段透传；迁移有效协议测试到 exec_command |
| AgentCommandsEnabled / AgentCommandRecipes 旧持久化字段 | 兼容读取后失效化，不再授予权限或执行，不暴露 UI，复制运行配置时不透传 |
| CommandRecipe / CommandResult、read_output、WindowsCommandRunner | 保留：现行终端、输出、取消及后台进程管理仍在使用 |
| run_command / list_commands 的历史渲染名称 | 保留旧聊天记录显示；生产工具列表/调度不提供旧命令，当前提示与操作摘要使用 exec_command |
| Build.ps1 的重复 shared/kernel 补源 | 删除，已有递归扫描覆盖；保留真正的测试 fixture 与 helper 明确依赖 |
| 模板、宣传截图、开发设计/发行文档 | 从运行包移出，源码/独立创作模板中保留 |
| 与本次无关的绘图脚本、必要过程材料 | 保留，未按“看似 AI 生成”直接删除 |

主要入口：`src/ReleaseVersion.cs`、`ReleaseIdentity.cs`、`GitHubReleaseChannels.cs`、`UpdateCoordinator.cs`、`UpdateInstall.cs`、`UpdateWorker.cs`、`AboutPanel.cs`、`LiveCallWindow.cs`、`PcmOutput.cs`；发行边界在 `tools/runtime_package.py`、`Package.py`、`BuildInstaller.ps1`、`installer/` 和 `.github/workflows/windows-release.yml`。

## 实际验证

所有日志均位于本实施目录的 `work/implementation/`，不进入运行包或提交。

| 验证 | 结果与证据 |
| --- | --- |
| Windows 测试目标 | 124/124 个目标有通过证据：完整首轮 123 组 + 新 LiveWaveformTests；首轮 2 组失败处理后复测通过。见 final-suite-results.json，首轮原始结果另存 suite-results.json |
| 首轮 PetRuntimeTests 失败 | 真实鼠标输入的时序超时；全量结束后独立复跑两次均 22 项通过，未改放宽断言或超时 |
| 首轮 ReleaseUiSmoke 失败 | 发现原设置页嵌入主窗口后问候预览被拦截，修复后通过；新增通道选择与保存检查，最终 29 项通过 |
| 更新即时操作 | AutoUpdateSmoke 44 项通过，含倒计时第 1/3/5 秒立即交接与重复点击仅启动一次、跳过五秒、复用下载、草稿/保存失败/取消/退出交接；正式与真实预览编译各运行通过 |
| 版本与身份 | ReleaseVersionTests、ReleaseIdentityTests、UpdateChannelTests、UpdateTests、UpdateApplyTests、UpdateScheduleTests 全部通过；覆盖真实 PE 同底版 dev→正式、commit/数字身份不符、安装已变化、精确清理/恢复 |
| 安装器拒绝规则 | InstallerVersionSmoke：6 组版本/修复/错误身份用例 + 1 组错误 commit 覆盖前拒绝通过 |
| 真实更新生命周期 | UpdateLifecycleSmoke 完整 40 项通过：ZIP 正常/中止/回执失败/损坏包、Inno 正常/故障、旧程序自动重启、配置/历史/皮肤保留及卸载元数据回滚 |
| 干净真实应用安装 | 用最终真实主程序/helper 和生产 Inno 脚本（仅替换独立 AppId/快捷方式组）安装；除卸载器自产物外，18 个运行文件与 ZIP 清单及全部字节一致，2 项通过，随后卸载 fixture |
| 真实历史助手 | v0.9.2、v0.9.3 原始引擎实际应用新精简 ZIP；另用真正 v0.9.3 helper 进程进行 0.9.3→0.9.4 的 owned probe 等待/交接/重启，6 项通过，并应用真实新编译 0.9.4 ZIP |
| 预览构建 | 独立副本设 0.9.4-dev.2 / AssemblyVersion 0.9.4.0，7 组更新回归、实际 App/ZIP/模板/源码/Inno 打包通过；最终共用 PE 验证器也接受此真实双 EXE |
| 打包 | PackagingSmoke 带真实 ISCC 及 v0.9.3 旧助手全部通过：干净源码构建、精确 18 文件、5 个角色包、完整模板、源码防私有文件、摘要和安装器编译 |
| 发布边界 | RuntimePackageTests 3 项、ReleaseAssetTests 4 项、WindowsReleaseTests 1 项含多组拒绝子场景通过；workflow YAML、嵌入 PowerShell、构建脚本、Python 语法检查及 diff --check 通过 |
| 无 Git 正式源码 | 通过 Git ceiling 隔绝父仓库，在真实干净源码副本构建 App；helper ProductVersion 为 0.9.3，共用打包验证器通过 |
| 安装记录识别 | UpdateRegistrationSmoke 9 项通过 |
| Mac/共享检查 | MacPackagingTests 15 项、PromoteReleaseTests 1 项含 7 个子场景通过；共享 .NET 10 回归程序成功（382 条 PASS）；Avalonia 桌面项目编译成功，0 警告/0 错误 |
| Live | LiveTests 29 项通过；LiveWaveformTests 验证实际绘制：恒定音量不同帧不同、静音帧保持一致、同回复不同 PCM 分块音量不同 |

ReleaseUiSmoke 有一条既有“自有窗口实际获得前台后”的全屏断言因 Windows 前台属于其他进程而按原测试逻辑跳过，其余 29 项通过。未把此跳过断言记为通过。

## 实际体积与附件身份

对比相同五位内置伙伴的公开 v0.9.3 与本地验证构建；单位为原始字节，不把源码图片原始大小冒充 ZIP 节省。

| 指标 | 已发布 v0.9.3 | 本地精简验证包 | 减少 |
| --- | ---: | ---: | ---: |
| 运行文件数 | 111 | 18 | 93 |
| ZIP 字节 | 209725158 | 184181263 | 25543895 |
| 解压后运行字节 | 218899126 | 188255258 | 30643868 |
| 安装器字节 | 208566095 | 185502928 | 23063167 |

实际干净安装的受管文件为 18 个，与 ZIP 一致；Inno 自带 unins*.exe/dat 不计入运行文件集合。ZIP 约节省 24.4 MiB（约 12.2%），安装器约节省 22.0 MiB（约 11.1%）。应用保留所有内置角色资源，不靠删减角色达成。

- 公开旧 ZIP：`59bbb0085c79f67ad7147e91dcce8f6f7c4790bd09f3a9f4b7c6e8aa666f4037`，来自 GitHub v0.9.3，已核对 API digest。
- 真正旧 helper：`b2da124f51b5c537e901461b79311a9cb740f5963bf73b9e3b19f10eb7196101`。
- 本地 ZIP：`410f58c3e9d3ca2bf54a1f2da1fca7041573ddf530aca8d6820b9d989960abd7`；安装器：`7bdc3c9996803745d7dc740fea28fd04809fe783b25f6b0e6222ff48e701414d`。
- 具体构建路径、尺寸和 SHA 见 `work/implementation/package-comparison.json`；独立预览/桥接产物在 `work/dev-smoke-source/work/artifacts/` 与 `work/bridge-smoke-source/work/artifacts/`。
- 此处包均为验证产物，构建时尚无最终发布提交/标签，不应分发或替换旧 Release；发行必须从未来干净标签重新构建。

## 审查修复与实施判断

完成一次独立全分支审查，重要问题已在同一修复轮中处理：安装前身份/失败恢复、预览默认及旧配置迁移、同底版旧 EXE 打包拒绝、helper 来源/自身绑定、发布失败关闭与串行 latest、精确历史文件规则。历史规则的中文路径使用 Git NUL 分隔读取，避免转义路径遗漏。没有已知未处理的 P1/P2 或延后的小问题清单。

实施中作出的判断：

1. 在单独 worktree 中按计划串行实现，只做一次独立最终审查，避免覆盖用户现有工作；代价是当前原目录 main 不会自动出现这些改动，需要用户之后决定合并方式。
2. 修复更新 Progress 回调跨线程访问 WPF：Windows .NET Runtime 堆栈已证实此前会崩溃；通知改为必要时进入 Dispatcher，代价是后台线程通知异步排队。
3. 无 Git 元数据的正式源码本地构建保留数字身份兼容；预览打包必须有 commit，正式发行必须通过干净标签 CI。代价是此类本地正式构建不能作为已验证发行包分发。
4. 修复设置页问候预览被嵌入主窗口阻止的既有缺陷：仅允许设置承载窗口中的手动预览通过，自动问候仍遵守原限制；代价是额外修改 PetWindow 一条条件并补跑问候/运行/UI 回归。
5. 不清空用户目录或递归删除历史树，使用真实发行的精确哈希清理与受管文件备份；代价是用户修改过的历史材料会保留，主动选择保护用户文件。
6. 保留分支和验证记录，不合并、不推送、不发布；本机工具仅解压到 work 内，未安装 Docker 或更改系统级 SDK。

## 原未提交改动保留记录

原 main 状态前后保持：`tools/BuildReadmeVisuals.ps1` 已修改（4 行增/4 行删）；未跟踪的两份 gugugaga 计划、本任务原计划、exports/、previews/gugugaga-v1/、tools/CaptureReadmeWorkspace.cs/.ps1 均仍在原目录。没有把这些原工作纳入本分支。更换 agent 后不要 reset、clean 或覆盖原目录，也不要误把 work 下的历史下载及测试产物发布。

## 验证边界与后续发布顺序

[未验证] 原生 macOS 双架构运行/签名/公证；本机只做共享回归、桌面编译与打包合同。没有推送或触发远端 CI，不能宣称远端 CI 已通过。

[未验证] 使用用户实际 Live 服务/音频设备的真人通话观感；已验证离线绘制、分块音量和 Live 生命周期。没有调用付费模型或使用用户密钥。

[未验证] 新正式/预览 Release 在公网中的整条往返切换，因为本次禁止发布。HTTP fixtures、版本比较、真实预览 PE/安装器、真实旧 helper 桥接已本地验证；未来发布后仍须重新下载公开附件验收。

后续只有用户明确要求发布才执行：核实远端 main/dev 与最新 Release → 选择未占用的更高纯数字正式桥接版本 → 合并并固定干净提交/标签 → 从该标签重建和验证 → 发布正式桥接版 → 同步兼容 updater 到 dev → 发布更高底版 dev.N（pre-release/latest=false）→ 重新下载公开附件核验哈希及更新路径。不得直接发 dev 让旧 helper 解析后缀，不得用自动降级撤回坏版本，应发更高修复版本。

复验从实施分支目录开始：

```powershell
Set-Location 'E:\workspace\codex\deskpet\work\update-channels-cleanup'
.\Build.ps1 -Target ReleaseVersionTests
.\Build.ps1 -Target ReleaseIdentityTests
.\Build.ps1 -Target UpdateChannelTests
.\Build.ps1 -Target AutoUpdateSmoke
.\Build.ps1 -Target LiveWaveformTests
python -X utf8 tests/RuntimePackageTests.py
python -X utf8 tests/WindowsReleaseTests.py
python -X utf8 tests/InstallerVersionSmoke.py --compiler 'E:\workspace\codex\deskpet\work\tools\inno\ISCC.exe'
$env:DESKPET_TEST_INNO='E:\workspace\codex\deskpet\work\tools\inno\ISCC.exe'
python -X utf8 tests/UpdateLifecycleSmoke.py
python -X utf8 tests/PackagingSmoke.py --compiler $env:DESKPET_TEST_INNO --legacy-worker work/implementation/legacy-0.9.3/DeskPet.Update.exe
```

完整 Windows 目标在 `.github/workflows/check.yml`；额外烟雾与共享入口已在上表列出。需要重建历史清理规则时使用完整 Git 历史运行 `python -X utf8 tools/generate_retired_runtime.py`，不要根据用户磁盘文件生成删除名单。源码清单、必要脚本和本报告均不进入运行包。
