# DeskPet 更新通道、防降级、立即重启与发行精简完整实施计划

> **实施状态（2026-10-21）：已完成代码与本地验收，尚未发布。先阅读同目录 [实施与交接报告](2026-10-21-update-channels-and-release-cleanup-results.md)；下面保留原始计划清单供对照，未勾选项不能直接推断尚未实现。原生 Mac 和未来公开 Release 的验收边界以报告为准。**

> 交接说明：本文包含需求、设计、源码证据、任务顺序、接口约定、发布策略和验收条件。接手 agent 不需要此前聊天记录。请先完整阅读本文，再核对仓库当前状态。
>
> For agentic workers: implement task by task with superpowers:executing-plans, or use superpowers:subagent-driven-development only when that execution method is authorized. Checkboxes record implementation progress, not work already completed.

**Goal：** 在 Windows 桌宠“关于”页提供正式版 / 预览版更新通道，禁止降级；允许就绪后立即更新重启；清理已确认冗余代码，并让安装包和免安装 ZIP 只包含运行所需内容。

**Architecture：** 扩展现有 AboutPanel → UpdateCoordinator → IUpdateFeed/GitHubUpdates → UpdateJob → UpdateWorker/UpdateInstall 链路。统一发行版本排序，分开发行版本和程序集身份；打包共用显式文件清单，不另造更新框架。

**Tech Stack：** Windows、C#、WPF、.NET Framework 4.8、PowerShell、Python/Pillow、Inno Setup、GitHub Releases/Actions。跨平台项目已有 .NET/Avalonia/macOS 打包，本次保证共享版本变更不破坏它们，不增加 macOS 自动安装更新。

**Spec：** 本文第 1–7 节即实施规格；第 8 节是实施任务，第 9–11 节是验证、交付和发布约束。

**状态：** 仅计划。尚未修改产品代码、执行构建/测试、创建版本标签或发布 Release。本文写于 2026-10-21，源码分析基于 2026-10-20 核实的固定远端 main 提交。

## 1. 仓库、基线与执行前保护

- 目标仓库：https://github.com/Eason4869/deskpet
- 本机目录：E:\workspace\codex\deskpet
- 已审查远端 main：318a82bf754b349445a08ed0bfc3bc3893e1dbe0。
- 该远端提交 DisplayVersion 为 0.9.3、AssemblyVersion 为 0.9.3.0。
- 本地 checkout 当时仍为 4ae351827325f0e54b090cbfabbe8d5804222838（0.9.2），落后 origin/main 5 个提交。不得直接拿本地旧文件替换远端新版本。
- 已观察到的用户改动：tools/BuildReadmeVisuals.ps1 已修改；docs/plans 下两份 gugugaga 设计文档、exports/、previews/gugugaga-v1/、tools/CaptureReadmeWorkspace.cs 和 tools/CaptureReadmeWorkspace.ps1 尚未跟踪。必须重新检查并保留，不能 reset、clean、覆盖或顺手提交。
- 前一阶段所在 astrbot 目录不是本次目标。不要在 E:\workspace\codex\astrbot 修改 DeskPet 产品代码。

执行前在 PowerShell 检查：

~~~powershell
Set-Location 'E:\workspace\codex\deskpet'
git status --short --branch
git remote -v
git rev-parse HEAD
git log -5 --oneline
git ls-remote origin refs/heads/main refs/heads/dev
~~~

再读取实际存在的 AGENTS.md。若 main 已前进，基于最新 main 重新核对本文定位；保留原验收条件，记录哪些实现已被上游覆盖。网络或权限问题不能通过擅自回退到旧基线解决。实现应使用隔离工作目录/分支，不能覆盖已有工作；隔离目录必须处于有写权限的路径。本文的新增计划文件也要保留。

## 2. 用户已经明确的需求与边界

### 2.1 必须完成

1. 审查并清理 main 中确认无用、重复、退役的代码；不能仅凭“像 AI 写的”或文件大就删除。
2. 源码仓库可保留必要测试、验证脚本、过程文件和素材制作工具。
3. Windows 安装包及免安装 ZIP 尽可能精简，不带开发过程文件、无关截图、完整外观创作模板等。
4. 用户已经确认：制作新桌宠的完整模板改为独立下载；设置中“打开模板文件夹”改为“下载制作模板”。现有角色和动画不受影响。
5. “关于”页新增正式版（main）/预览版（dev）选择，允许跨通道升级，禁止降级。
6. 明确允许：0.9.1 → 0.9.2-dev；0.9.2-dev → 0.9.3。
7. 明确禁止：0.9.3 → 0.9.2-dev；0.9.2-dev → 0.9.1。
8. 手动“检查并更新”和后台自动更新下载完成后，仍按现有保护条件等待空闲，再倒计时 5 秒。
9. 就绪/倒计时期间点击“立即更新并重启”，跳过剩余 5 秒，立即进入安装交接；不能重新开始 5 秒。
10. 本次交付首先是此完整计划；实施、推送和发布是后续阶段，不得因阅读本文就立即发布。

### 2.2 本计划采用的明确默认值

以下用于消除交接歧义，是设计默认，不冒充用户逐条口述：

- 正式通道只接收正式 Release；预览通道接收预览版和更高正式版。这样 dev 用户也能正常升级到之后的正式版。
- 首次启动旧配置默认选择正式通道；新安装 dev 构建且没有通道配置时默认预览通道。已保存的用户选择优先，跨通道升级不得擅自改回默认值。
- 通道选择是“更新偏好”，不是任意历史版本下载器。界面显示当前安装版本、所选通道、可用目标版本。
- 切通道只保存偏好并清空旧通道检查时间；不立即下载或退出。后续按已有自动检查调度运行，或用户点“检查并更新”。
- 下载、待安装、倒计时、安装交接期间禁用通道选择；取消更新后才可更改。另用通道快照/任务身份防止异步旧结果回写。
- 若切到正式通道而当前 dev 高于最新正式版，显示“正式通道暂无更高版本，保留当前版本”，等待正式版追上，绝不安装旧正式版。
- 同版本不作为应用内更新目标。新安装器可允许同版本修复安装，但必须拒绝低版本。
- “立即”仅跳过人为倒计时，不绕过数据保存、对话/Agent/语音/草稿保护、包校验和 helper 握手；不强杀正在运行的任务。
- 新功能 UI 和自动安装覆盖 Windows WPF。macOS 此次只维护共享版本、显示和打包兼容性，继续既有人工下载流程。
- 不引入 Docker、额外常驻服务或新的通用更新框架；保持现有 UI 风格。

## 3. 已确认的代码事实与静态审查结果

行号对应上述远端固定提交，后续以符号定位为准：

| 优先级 | 位置 | 事实 / 最小处理 |
| --- | --- | --- |
| P1 | installer/DeskPet.iss:77–84；Package.py:92–98、126–129 | ZIP 按 runtime_plan 精确归档；安装器递归通配旧输出目录的 templates/previews；复制阶段不清旧文件，导致两种包分叉。改为同一显式清单和全新 staging。 |
| P2 | Package.py:62–83、137–139 | 完整两代外观模板随运行包分发，另有独立模板 ZIP。移出运行包，独立附件保留。 |
| P2 | Package.py:57–61 | previews 顶层图片无引用筛选全部进入运行包。改用面向安装用户的简版说明，删除运行包截图依赖；仓库 README 和图片保留。 |
| P2 | src/AgentCommand.cs:27–57；ConversationSession.cs:353；AgentWorkspace.cs:102；WorkSettingsPanel.cs:120 | 旧 recipe 命令链仍存在，但生产入口 ConfigureCommands(false,"[]")、桌面工具过滤以及设置保存共同禁用它。淘汰旧链专用逻辑及透传，迁移仍有效的测试。 |
| P3 | Build.ps1:19、23–24、34–45 | App 和测试分支已经递归收集整个 src，后续逐项补 shared/context/prediction/kernel 属重复历史补丁。删除重复补齐段，保留测试 fixture 依赖。 |
| P3 | Package.py:44；installer/DeskPet.iss:65、74–75 | native-agent-design.md、roadmap.md、release-naming.md 为开发/发布文档，移出运行包但留在仓库。其余文档按实际入口和依赖审查。 |

更新链路现状：

- AboutPanel.cs:43 调用 CheckAndUpdateAsync(true)，Refresh 在就绪时把按钮改成“立即安装并重启”。
- UpdateCoordinator.cs:70 附近就绪分支把 restartAt 设为 null；ApplyWhenReady 在约第 107 行又 AddSeconds(5)。所以当前按钮会重新等待。
- GitHubUpdates.CheckAsync 只请求 /repos/{owner}/{repo}/releases/latest。
- UpdateRelease.IsNewerThan、Coordinator、ReleaseInfo、UpdateInstall、UpdateWorker 均依赖数字 System.Version；只改标签正则远远不够。
- UpdateInstall.Validate 在约第 77 行禁止低/同版本，约第 137/175 行比较包内/安装后的数字程序集版本。
- UpdateWorker.CheckVersion 在约第 67–68 行将 InstalledVersion 当成程序集数字版本。
- BuildInstaller.ps1:12 对 DisplayVersion 直接转换 [version]；tools/release_assets.py 也只接受纯数字。
- tools/mac_package.py、tools/promote_macos_release.py 以及 macOS 构建参数有纯数字发行版本假设。
- PetWindow.CanApplyUpdate(bool manual)、PrepareUpdateAsync 已有活动保护与设置/历史落盘流程，必须复用。

明确不能误删：

- src/AgentCommand.cs 中当前终端仍共用的 AgentCommand / CommandResult、commandResults、超时属性和 ReadOutput。
- assets/additional 原图、旧 interactions 图集：AdditionalInteractions.LoadOriginal 和 PetWindow 的随机动画变体仍在实际读取。它们不是已证实无用素材。
- AgentProviderRegistry 仍被 AgentCapabilityResolver 使用，不能因某配置入口仅在测试出现而整删。
- 第三方许可、运行时依赖、内置角色资源、升级 helper、必要用户教程。

## 4. 版本模型：统一排序，单独校验二进制身份

### 4.1 对外版本

今后新发布统一：

- 正式：0.9.4，标签 v0.9.4。
- 预览：0.9.5-dev.1、0.9.5-dev.2，标签分别 v0.9.5-dev.1、v0.9.5-dev.2。
- 用户例子中的 0.9.2-dev 作为可读取的旧格式预览号，排序为 dev 序号 0；新包一律发布 dev.N，N 从 1 递增。
- 同版本禁止覆盖 Release、移动标签或替换成不同二进制；修复后必须新版本或新的 dev.N。
- 仅接受明确支持的版本格式，不从任意标签里“提取看起来像数字的部分”。未知格式不自动安装。

顺序为先比较主/次/修订数值，再比较同底版正式/预览，再比较 dev 序号；同底版正式高于所有 dev。不能按字符串、发布时间或去掉后缀后比较。

| 当前 → 目标 | 结果 |
| --- | --- |
| 0.9.1 → 0.9.2-dev / 0.9.2-dev.1 | 允许 |
| 0.9.2-dev → 0.9.3 | 允许 |
| 0.9.3 → 0.9.2-dev.10 | 拒绝 |
| 0.9.2-dev.1 → 0.9.1 | 拒绝 |
| 0.9.4-dev.2 → 0.9.4-dev.10 | 允许 |
| 0.9.4-dev.10 → 0.9.4 | 允许 |
| 0.9.4 → 0.9.4-dev.11 | 拒绝 |
| 0.9.4-dev.2 → 0.9.4-dev.2 | 不更新 |
| 0.9.9 → 0.10.0-dev.1 | 允许 |

保留旧代码已支持的三段/四段纯数字版本读取兼容：缺失第四段归一化为 0；旧非零第四段沿用数字比较次序，新版本不再发布四段对外版本。比较键依次为 major、minor、patch、legacyRevision、正式级别、devNumber；预览格式不接受第四段数字。构建用数值段必须符合 .NET/安装器数字版本范围，不静默截断、溢出或改写。

### 4.2 数字程序集与完整发行身份

- DisplayVersion 使用完整对外版本，如 0.9.5-dev.2。
- AssemblyVersion / AssemblyFileVersion 保持数字，例如两者均为 0.9.5.0；不拿它们决定预览与正式的先后。
- 新构建写入 AssemblyInformationalVersion，包含完整发行版本和提交 SHA；同底版不同 dev 通过它及下载包 SHA-256 区分，不能仅凭 0.9.5.0 判为同一构建。
- 版本比较忽略已验证的信息版本构建元数据；二进制身份检查必须比对完整声明及提交身份。读取 PE 元数据，不能执行包内程序来查询身份。
- CLR 数字版本只负责校验基底一致；UpdateJob.InstalledVersion / TargetVersion 继续表示完整发行版本。
- UpdateJob 增加 ProtocolVersion、InstalledAssemblyVersion、TargetAssemblyVersion、InstalledBuildId、TargetBuildId；应用、helper 和包身份必须绑定一致。新增任务使用协议 2；协议 1 仅接受历史纯数字版本，并保留旧校验规则。
- 不为版本比较引入重量依赖。新增单一 ReleaseVersion.cs，应用与独立更新助手都编译同一份；Python 发布脚本使用同一套固定测试向量验证等价规则。

### 4.3 防降级位置

必须在候选选择、下载前、pending 就绪、helper 接手前及实际替换前重复验证目标严格高于当前；helper 重读当前安装身份，不能只相信 job.json。

新安装器也要读取已安装程序的发行身份并拒绝低版本安装；无可读取身份而已有目标程序时明确失败或要求用户选择新目录，不能猜测为低版本。允许同版本修复安装。旧版仅有数字版本时走历史兼容规则。

边界：无法让已经发布到用户手里的历史安装器自动获得新防降级能力。本文保证应用内新链路和新安装器；不能声称用户双击任意旧安装器也一定被阻止。

## 5. 更新通道与 Release 发布合同

### 5.1 客户端发现规则

- UpdateChannel 枚举为 Stable / Preview，PetSettings.UpdateChannel 持久化；缺字段按第 2 节默认，非法枚举归正式通道。Settings.Copy/保存/加载和 UI 草稿路径必须完整传播。
- IUpdateFeed.CheckAsync 增加明确 UpdateChannel 参数；同步迁移实现、fake feed、镜像探测和所有调用点。镜像测速继续可使用正式包探测，但不能改变用户更新通道。
- 从 Releases 列表获取候选并分页，不能用 /latest 获取 dev。每页 100 条，跟随下一页，最多 10 页；达到上限仍有下一页时明确报告检查范围不足，不静默声称已是最新。
- 忽略 draft；正式候选要求无 dev 后缀且 prerelease=false；预览候选要求 dev 格式且 prerelease=true。标签与标志矛盾时不作为自动更新目标。
- Stable 从正式集合选语义最高版本；Preview 从正式与预览的并集选语义最高版本。发布时间乱序不影响结果。
- 最高目标缺少匹配的当前平台/安装模式附件或可信 SHA-256 时显示“目标发行不完整”，不静默安装别的版本。
- 只允许本仓库标签对应的精确附件 URL、精确版本与安装模式；不放松现有 URL/路径/摘要/大小校验，不接受源码 ZIP、皮肤包、模板包或 macOS 附件。
- 选定候选后，查询本仓库该 tag 的 Git ref，剥离 annotated tag 至实际 commit（最多 4 层，循环/超限失败），得到 TargetBuildId 所需 SHA；不能把 release.target_commitish 当作可靠构建 SHA。轻量标签直接使用 commit。把规范发行号与 SHA 组成预期 informational version，与包内 PE 元数据比对。该解析只对最终候选执行，不对每个历史 Release 都额外请求。
- 禁止自动降级；选择落后的通道仍保存偏好，只报告没有可升级目标。
- 检查/下载开始时捕获通道和操作序号；取消/退出后晚到结果无权创建 pending。通道 UI 忙时禁用，但底层仍校验捕获值。

GitHub 的 /latest 不包含预发布，且预发布不能标为 latest；main/dev 分支名称本身不是客户端发行通道协议。通道靠规范标签、prerelease 标志及构建发布检查建立。

### 5.2 发行表与文件名

| 项目 | 正式示例 | 预览示例 |
| --- | --- | --- |
| 构建来源 | main 上固定 SHA | dev 上固定 SHA |
| Tag | v0.9.4 | v0.9.5-dev.1 |
| Release 标题 | DeskPet 0.9.4 | DeskPet 0.9.5-dev.1 |
| prerelease | false | true |
| make_latest | true（发布时检查版本不回退） | false |
| Windows 安装器 | DeskPet-Windows-Setup-0.9.4.exe | DeskPet-Windows-Setup-0.9.5-dev.1.exe |
| Windows 便携包 | DeskPet-Windows-0.9.4.zip | DeskPet-Windows-0.9.5-dev.1.zip |
| 源码附件 | DeskPet-Source-0.9.4.zip | DeskPet-Source-0.9.5-dev.1.zip |
| 完整模板附件 | DeskPet-Skin-Template-v2-0.9.4.zip | DeskPet-Skin-Template-v2-0.9.5-dev.1.zip |

皮肤包同样使用完整发行版本后缀。保留 SHA256.txt，逐项列出实际发布文件。模板下载入口使用 Release 页面/明确模板附件地址，不让创作模板进入自动更新附件候选。

发布 workflow 必须验证 tag 解析、版本常量、informational version、构建 SHA、分支归属、prerelease 与附件一致。若 tag 已存在，解析实际 commit；不能仅信 target_commitish 字符串。不要每次 dev push 就自动公开 Release，默认人工触发/打明确 dev.N 标签后发布。

### 5.3 旧客户端桥接

旧客户端使用其本机旧 helper，它在下载后、程序退出前就要解析目标版本。新包里有新 helper 不能修复首次升级。

1. 先发布一个正式纯数字“兼容桥接版”，内含全部新版本/通道/helper 能力，仍使用旧版可接受的 Windows 文件名、程序集 x.y.z.0 和旧运行目录布局。
2. 0.9.3 是审查基线，不指定它为尚未发布的桥接版。0.9.4 仅为示例；执行发布时查询当前已有版本，选择尚未使用且更高的正式版本。
3. 旧客户端先经 /latest 升级桥接版，再选择 Preview 接收 dev.N。
4. 桥接版不能新增旧 helper 不允许的 ZIP 根文件/扩展名，不能超出历史包大小上限；新 identity 放程序集元数据，新 staging 清单只作构建输入，不塞进便携 ZIP。
5. 后续正式版继续保持数字程序集基底与旧 worker 对纯数字版本的校验兼容；新客户端负责 dev 路径。
6. 不为绕过旧 helper 发布“表面数字、实际 dev”的正式 Release，不把预览标成正式或 latest。

### 5.4 macOS 兼容

macOS 文件名已有 -preview，表示签名/发布成熟度，不等于本次 dev 通道。必须将“发行版本后缀 dev.N”和原有 macOS 签名模式分开；不能因 Windows 正式版就宣称 Mac 已 Developer ID 签名/公证。

tools/mac_package.py、installer/macos/build.sh、desktop/Directory.Build.props、相关 csproj、macOS 关于页及 tools/promote_macos_release.py 的版本读取需要审计。完整 DisplayVersion 用于显示/文件名；CFBundleShortVersionString、CFBundleVersion 和程序集字段使用适合平台的数字值。没有新增 Mac 渠道发布需求时，promotion 仍明确只接受正式版本并对 dev 给出说明，不可被改正则后误发布。

## 6. “立即更新并重启”状态机

| 状态/操作 | 预期行为 |
| --- | --- |
| 空闲点击“检查并更新” | 检查、下载、校验 → 等待安全空闲 → 5 秒倒计时 |
| 自动更新下载完成 | 等待既有自动空闲条件 → 5 秒倒计时 |
| 就绪或倒计时中点“立即更新并重启” | 标记用户明确立即安装，按手动安全条件检查；安全时直接交接，跳过剩余倒计时 |
| 立即更新时仍有任务/草稿/保存错误 | 显示具体阻塞原因；保留立即意图，安全条件满足即交接，不强杀、不丢草稿、不再加 5 秒 |
| 进入倒计时后又忙碌 | 普通自动/手动流程重置倒计时，重新安全空闲后完整等 5 秒 |
| 点击“取消更新” | 取消网络/清 pending、deadline、立即意图、timer；当前版本继续运行 |
| helper 启动或校验失败 | 不退出当前程序，清理交接状态并显示原因，允许重新检查 |
| 已在安装交接时连点 | 忽略重复请求，只启动一个 helper |

实现以显式 InstallPendingNowAsync() 区分“检查”和“安装已下载包”；不要再复用 CheckAndUpdateAsync(true) 的就绪分支重置 deadline。普通倒计时和立即操作共用一次 TryApplyPendingAsync() 交接实现，设置 applying 后再 await，防止 timer/click 竞态。

继续复用 PetWindow.CanApplyUpdate(manual)、PrepareUpdateAsync、helper ready 握手、交接后再次验证和失败 Abort。立即点击允许使用现有手动更新对已打开窗口的规则，但其余任务/草稿保护不能解除。网络设置无需再次通过测速才能安装已经下载并校验的 pending。

## 7. 发行精简与代码清理边界

### 7.1 统一运行文件清单

- Package.py 生成明确的运行文件计划；ZIP 和 Inno 使用同一计划，不各自维护一套用户文档清单。
- 新增 tools/runtime_package.py 承担计划收集、路径校验、staging 和生成 Inno [Files] include；不再新增另一个泛用 helpers 模块。
- staging 必须是本次构建创建的独立临时目录，读取已构建程序后仅复制 allowlist 文件。禁止清空真实安装目录或复用旧 APP 目录作为通配输入。
- 生成的 Inno include 对每个文件使用精确 Source/DestDir。移除 templates/previews 的 recursesubdirs 通配，不靠后缀黑名单兜底。
- 不把 build 清单、Inno include、验证日志、临时文件写进运行 ZIP；运行包根布局继续满足旧 helper。
- 新用户 README 由源码 templates/runtime-readme.md 提供：启动、数据位置、配置、更新通道、模板下载、在线帮助。不要复制网站宣传 README 再做大量 replace。
- 运行包不再携带完整外观制作模板、宣传截图。用户必需文档及其必要本地引用必须完整；第三方许可不能删除。
- 独立模板 ZIP 保留原始创作文件（包括动作表、提示词等）；源码 ZIP 保留必要测试/脚本，不以精简运行包为由删源码历史证据。
- 运行包文本资源集合改变后同步更新 PackagingSmoke 的旧模板硬断言，改为“运行包无完整模板、独立模板包完整、入口有效”。

### 7.2 已安装目录的旧文件

只缩小 ZIP 不会自动移除以前安装的 docs/templates/previews。实施安全的旧发行文件清理：

- 只处理已知历史发行清单中的精确相对路径，并验证文件内容仍等于历史发布哈希；用户改写或新增的文件一律保留。
- 已知旧文件删除前纳入与应用替换相同的备份/回滚；新安装器同样遵守该范围。目录仅在为空且确证为普通目录时删除。
- 路径解析必须留在安装根内，拒绝重解析点/符号链接；禁止 Remove-Item templates -Recurse 之类整目录清空。
- 数据目录、配置、聊天、导入外观、用户自建文件均不在清理范围。
- 无法取得某旧版本的可信清单/哈希时，保留旧文件并在报告中说明。不得为了宣称“升级目录完全精简”删除来源不明内容。

### 7.3 旧 recipe 和构建补丁

旧 run_command/list_commands 链条删除前从整个仓库检查调用，包括跨平台链接源、工具 schema、持久化、项目设置、测试和文档。移除专属解析/执行/配置 UI 透传；旧持久化字段只读兼容后忽略，不让读取旧数据失败。

保留 exec_command/write_stdin/read_output 所需 DTO、输出集合、超时、取消和 WindowsCommandRunner。旧协议/工具回归测试改用现行 exec_command，只有纯旧功能测试可以删除；不能靠删测试掩盖有效功能回归。

Build.ps1 仅删除递归扫描之后的重复补源段。新增 ReleaseVersionTests 等目标要在 ValidateSet 和 CI 注册；独立 helper 的 workerSources 必须增加 ReleaseVersion.cs 及真正依赖的元数据读取文件。

## 8. 任务拆分与交接接口

每个任务按“补有效失败测试 → 确认失败 → 最小实现 → 运行相关测试 → 审查 diff”完成。文件名是计划约定；若上游已有等价实现，复用它并记录映射，不复制一套。

### Task 0：建立安全实现基线

**输入：** 本文、最新 main/dev、当前本地状态。
**输出：** 隔离分支、固定实现 SHA、保留用户改动的记录。

- [ ] 完成第 1 节命令，核对远端和本地差异，读取 AGENTS.md。
- [ ] 建立隔离工作目录，不能在旧 0.9.2 基线上直接实施。
- [ ] 重查第 3 节问题，标记上游已修复项，记录基线。
- [ ] 先复现更新/打包现有测试结果；失败要定位阶段，不能先大改。

### Task 1：统一版本、构建身份和附件命名

**新增：** src/ReleaseVersion.cs、src/ReleaseIdentity.cs、tests/ReleaseVersionTests.cs。
**修改：** src/ReleaseInfo.cs、src/Program.cs、src/UpdateWorker.cs、Build.ps1、BuildInstaller.ps1、tools/release_assets.py、tests/ReleaseAssetTests.py；共享桌面版本属性/打包脚本。
**接口：** ReleaseVersion.TryParse(string, out ReleaseVersion)、IComparable<ReleaseVersion>.CompareTo；string Canonical、bool IsPreview、Version AssemblyBase 属性。ReleaseIdentity.Read(string executablePath) 返回发行版本、数字程序集版本、完整 informational version 和 CommitSha；新格式必须满足 informational version = Canonical + "+" + 40 位小写提交 SHA。对旧程序允许 Read 返回仅数字身份，但仅能进入本文明确的 legacy 分支。ReleaseInfo 的附件命名/版本校验全部复用 ReleaseVersion；Python 使用同一测试向量。ReleaseIdentity 只做静态 PE 元数据读取，不执行目标程序。

- [ ] 写第 4 节所有比较断言、非法输入、溢出、dev.2/dev.10、v 前缀、历史数字版本、显示版本/数字基底不一致断言。
- [ ] 在 Build.ps1/CI 注册 ReleaseVersionTests 后运行，确认未实现前失败原因正确。
- [ ] 实现统一排序与 InformationalVersion；正常 C# 主程序与 UPDATE_WORKER 分别声明自身元数据，不能产生重复 assembly attribute；workerSources 明确加入 ReleaseVersion.cs、ReleaseIdentity.cs。
- [ ] 更新 Python、Inno 参数和跨平台版本读取，纯数字程序集与完整 DisplayVersion 分开。
- [ ] 更新精确资产名识别，确保 Source/模板/皮肤/Mac ZIP 永远不被视为 Windows 自动更新包。
- [ ] 运行版本、ReleaseAssetTests 及现有 MacPackagingTests/PromoteReleaseTests；正式版旧命名仍通过。

### Task 2：通道发现、持久化和“关于”页

**修改：** src/Settings.cs、src/AboutPanel.cs、src/GitHubUpdates.cs、src/UpdateCoordinator.cs；tests/UpdateTests.cs、tests/UpdateScheduleTests.cs、tests/ReleaseUiSmoke.cs。
**接口：** UpdateChannel { Stable, Preview }；IUpdateFeed.CheckAsync(repository, useProxy, proxyAddress, preferPortable, channel, cancellationToken)；UpdateRelease 增加 CommitSha 和 TargetAssemblyVersion，前者由实际 tag commit 解析，后者由 ReleaseVersion.AssemblyBase 计算；全部调用点同步迁移。
**依赖：** Task 1 的 ReleaseVersion。

- [ ] 写旧配置默认值、dev 新安装默认、用户选择保持、无效枚举和 Settings.Copy 往返断言。
- [ ] 用本地 fake HTTP feed 测分页、乱序时间、草稿、标志冲突、缺附件、超限/网络错误、跨通道无高版本、语义最高候选；另测轻量/annotated tag、错误 commit、剥离循环和超限。
- [ ] 实现第 5.1 节规则，保留网络超时、镜像、代理和 SHA-256 保护；不为测试调用真实模型或发消息。
- [ ] “关于”页沿现有控件样式加入通道选择及版本提示；Preview 提示“可能包含未稳定功能”。
- [ ] 更新中禁用通道；取消后允许切换；操作序号拦截旧异步结果。
- [ ] 运行 UpdateTests、UpdateScheduleTests、ReleaseUiSmoke；覆盖界面重开、重启后选择保持。

### Task 3：应用与 helper 双重防降级、旧协议桥接

**修改：** src/UpdateInstall.cs、src/UpdateWorker.cs、src/UpdateCoordinator.cs、installer/DeskPet.iss；tests/UpdateApplyTests.cs、tests/UpdateTests.cs、tests/LegacyUpdateSmoke.py、tests/UpdateRegistrationSmoke.py。
**接口：** UpdateJob 采用第 4.2 节新增字段；协议 1 与 2 分开验证；发行排序与数字/构建身份分别验证。
**依赖：** Task 1、2。

- [ ] 写完整升级矩阵，同时针对 feed、job Validate、helper 和新安装器测试，不只测 UI。
- [ ] 增加 same-base dev→stable：两边 AssemblyVersion 都可能是 x.y.z.0，仍必须正确识别并升级。
- [ ] 写安装中程序被替换、job 的版本与 PE metadata 不一致、包 SHA 不符、错误通道、外仓库 URL、错误模式附件的拒绝断言。
- [ ] 实现 protocol 2，读取安装前、stage 内、安装后发行身份；交接前与替换前重验，失败不退出/恢复旧程序。
- [ ] 实现新 Inno 版本拦截；普通覆盖规则不能以 ignoreversion 绕过发行版本检查。同版修复按第 2 节处理。
- [ ] 使用真实历史已发布 helper 验证数字桥接包，不以当前 helper 冒充旧版。记录旧 worker 版本、哈希和来源。
- [ ] 运行 UpdateApplyTests、UpdateTests、LegacyUpdateSmoke 和安装器隔离目录验收。

### Task 4：5 秒倒计时和立即交接

**修改：** src/UpdateCoordinator.cs、src/AboutPanel.cs；tests/UpdateScheduleTests.cs、tests/AutoUpdateSmoke.cs、tests/UpdateLifecycleSmoke.py。
**接口：** Task CheckAndUpdateAsync(bool manual) 只负责检查/下载；Task InstallPendingNowAsync() 负责用户立即意图；内部 Task TryApplyPendingAsync() 是唯一安装交接入口。
**依赖：** Task 3 的 pending/任务身份。

- [ ] 增加第 6 节状态测试，至少覆盖倒计时第 1/3/5 秒点击、连点、timer/click 同时触发、忙碌后恢复、取消、helper 拒绝、PrepareUpdateAsync 失败。
- [ ] 复用现有可注入 apply 委托；对时钟/截止时间建立小型内部测试接缝，避免靠真实睡满 5 秒写脆弱断言。
- [ ] 实现普通手动/自动仍等 5 秒，立即意图不清成普通倒计时；重入最多一个 helper。
- [ ] 就绪按钮显示“立即更新并重启”，处理已下载包不依赖 SaveNetwork；检查新包时仍验证网络设置。
- [ ] 运行相关测试并 WPF 实测一次：不丢草稿、未保存设置或历史，不强行终止任务。

### Task 5：运行发行包清单与模板独立下载

**新增：** tools/runtime_package.py、templates/runtime-readme.md。
**修改：** Package.py、BuildInstaller.ps1、installer/DeskPet.iss、src/SettingsPage.cs、tests/PackagingSmoke.py；必要时 src/UpdateInstall.cs 的精确旧文件清理/回滚。
**接口：** collect_runtime_files(project_root, app_root) 返回有序的 source/destination 文件记录；stage_runtime(plan, staging_root) 仅向新建 staging 复制；write_inno_files(plan, staging_root, include_path) 生成精确安装文件项。三者不访问用户数据、不修改真实 app_root。
**依赖：** Task 1 的命名和 Task 3 的旧 helper 兼容约束。

- [ ] 在复用输入目录的 templates/previews 下植入旧 png、私有 md、子目录哨兵；安装器 staging 和 ZIP 都不得包含。
- [ ] 写运行 ZIP 与实际安装文件集合一致断言，排除卸载程序/安装器自生成日志等安装器自产物。
- [ ] 实现全新 staging 与统一清单；Inno 不再对旧目录递归搜文件。
- [ ] 完整模板、开发文档和宣传图移出运行包；独立模板 ZIP 完整度及源码验证脚本保留。
- [ ] “下载制作模板”打开当前版本对应的 Release 页面，附说明寻找 Skin-Template-v2 附件；无网络仅报清晰错误，不影响现有外观使用。
- [ ] 实现旧发行文件精确哈希匹配清理和回滚，验证用户修改/新增文件、重解析点、数据和导入皮肤被保留。
- [ ] 重写旧模板随包硬断言，验证 runtime-readme 的所有本地链接、许可和必要教程。
- [ ] 完整运行 PackagingSmoke（带 Inno compiler）、LegacyUpdateSmoke；记录新旧 ZIP、安装器、安装后文件数和体积，分别报告，不能用原始图片大小冒充实际节省。

### Task 6：删除退役 recipe 链与重复构建补丁

**修改：** src/AgentCommand.cs、src/AgentWorkspace.cs、src/AgentToolCatalog.cs、src/ConversationSession.cs、src/WorkSettingsPanel.cs、src/Settings.cs、项目设置映射实际所在文件、Build.ps1；tests/AgentCommandTests.cs、tests/AgentProtocolTests.cs 和相关终端/跨平台测试。
**依赖：** Task 1 修改 Build.ps1 后再删补丁，避免漏 worker 新依赖。

- [ ] 全仓搜索 ConfigureCommands、AgentCommandsEnabled、AgentCommandRecipes、run_command、list_commands，建立每个引用的去留表。
- [ ] 将仍验证协议、输出、取消等有效行为的旧 recipe 测试迁到 exec_command；给读取旧设置增加兼容断言。
- [ ] 删除 recipe 专属执行/校验/schema/字段透传，保留第 7.3 节现行共享数据和终端能力。
- [ ] 删除 Build.ps1 重复补源阶段；保留实际测试 fixture 追加和 helper 明确源码列表。
- [ ] 搜索残留：允许只读迁移字段和历史文档提及，不允许生产 schema/执行分支继续暴露旧工具。
- [ ] 运行 AgentProtocolTests、AgentCommandTests（迁移后）、AgentTerminalTests、WindowsTerminalTests、AgentReliabilityTests、相关持久化/跨平台测试；正式编译 App。

### Task 7：发布工作流、文档与整体复核

**新增：** .github/workflows/windows-release.yml（若执行时已存在相同职责的 workflow，直接扩展它）。
**修改：** .github/workflows/check.yml、tools/release_assets.py、BuildInstaller.ps1、docs/release-naming.md、docs/installation.md、docs/daily-use.md、docs/skin-format.md、README.md、CHANGELOG.md；必要的 macOS 版本读取文件。
**依赖：** Task 1–6。

- [ ] workflow 接收明确 tag/ref，固定 commit，验证 main/dev 归属和标签身份；构建只用干净 checkout。
- [ ] 完成正式/预览命名与标志合同检查；从草稿构建、验证完整附件/摘要后才进入发布步骤。
- [ ] 正式版最新标记不允许倒退；dev 永不置 latest；附件逐项验证后统一生成 SHA256.txt。
- [ ] 源码保留本计划/必要验证脚本，但不能进入运行安装包；文档解释模板独立下载和通道切换不降级。
- [ ] 检查 Mac 数字版本/显示版本/签名模式没有混淆；运行 MacPackagingTests、PromoteReleaseTests 和既有 macOS CI。
- [ ] 整体 diff 审查：无无关格式化/重命名、无重造 helper/协议、无用户工作丢失。
- [ ] 完成第 9 节整体验收与第 10 节交付报告；获得后续发布授权前仅保留可审查代码和本地构建产物，不创建公开 Release。

## 9. 验证命令与完成条件

在实施分支工作目录运行。以下为计划命令，写本文时未运行；新 ReleaseVersionTests 目标必须先完成注册。不能把尚未存在的目标失败当作环境故障。

~~~powershell
.\Build.ps1 -Target ReleaseVersionTests
python tests/ReleaseAssetTests.py
.\Build.ps1 -Target UpdateTests
.\Build.ps1 -Target UpdateApplyTests
.\Build.ps1 -Target UpdateScheduleTests
.\Build.ps1 -Target ReleaseUiSmoke
.\Build.ps1 -Target AutoUpdateSmoke
.\Build.ps1 -Target AgentProtocolTests
.\Build.ps1 -Target AgentCommandTests
.\Build.ps1 -Target AgentTerminalTests
.\Build.ps1 -Target WindowsTerminalTests
.\Build.ps1 -Target AgentReliabilityTests
.\Build.ps1 -Target App
python tests/MacPackagingTests.py
python tests/PromoteReleaseTests.py
~~~

完整安装包验收先定位 Inno ISCC.exe，不安装不明软件。项目原先支持 work\tools\inno\ISCC.exe 或系统 Inno Setup 目录；若缺少编译器，明确报告“安装器实际验证阻塞”，不能只检查 .iss 文本就算通过。按 PackagingSmoke 当前 --help 确认参数后执行：

~~~powershell
python tests/PackagingSmoke.py --compiler '实际已核实的ISCC.exe绝对路径' --legacy-worker '实际历史DeskPet.Update.exe绝对路径'
~~~

其余 UpdateLifecycleSmoke、UpdateRegistrationSmoke 和 LegacyUpdateSmoke 先读取脚本/--help，再以真实测试产物填入参数；不能拿生产安装目录当测试 sandbox。旧 worker/包来源和哈希写入验收报告。独立安装器一般为当前用户安装，不应要求管理员；如测试环境需操作注册表/系统目录，先明确实际修改范围和权限，优先使用临时用户级安装目录。

必须验收的用户路径：

- [ ] 旧已发布正式版 → 数字桥接版（真实旧 helper），再切 Preview → dev.N → 更高正式版。
- [ ] 同底版 dev.2 → dev.10 → 正式版；正式版拒绝同底版 dev。
- [ ] dev 切 Stable 而 stable 较旧：偏好保存、当前版本不变、明确无可升级版本。
- [ ] 手动和自动下载后仍等 5 秒；第 1/3/5 秒点立即后不再重新等待。
- [ ] 点击立即时任务/草稿/历史未保存：不退出；安全后立即交接。
- [ ] 下载取消/断网/摘要不符/助手失败/安装失败：当前程序或备份可恢复，数据不丢。
- [ ] 更新中不能换通道；异步旧结果、重复点击和 timer 竞争不会重复安装。
- [ ] 安装版只取安装器，便携版只取对应便携 ZIP；错误平台/模式/版本附件全部拒绝。
- [ ] 新安装器阻止降级，同版修复保持数据；历史安装器限制如实披露。
- [ ] 干净安装和便携解压均无模板大图、宣传截图、开发脚本、测试和报告。
- [ ] 升级清理旧文件只处理哈希匹配的发行内容；用户修改/自建内容保留；失败能回滚。
- [ ] 所有现有角色、动画、聊天、Agent、语音及设置入口正常；模板下载可达。
- [ ] 全部已有 Windows CI 目标通过；共享 Mac 构建/打包检查通过；无法本机实测的项目明确标未验证。

## 10. 发布顺序、交付物与回滚

实施完成交付：

1. 功能与清理代码 diff、有效回归测试、README/CHANGELOG 和发行文档。
2. 正式/预览发布 workflow 以及明确的桥接策略。
3. 新旧运行 ZIP/安装器文件列表、尺寸、SHA-256、CI 结果、安装/便携真实更新证据。
4. 用户改动保留记录；不能仅给“测试通过”而不说明哪些阶段实际运行。

正式发布必须在用户后续明确要求发布时执行：

1. 查询最新远端 main/dev、现存标签和 Release，选择未使用的新正式桥接版本。
2. main 固定 SHA 构建桥接版，保持旧 helper 兼容；先验证真实旧版本升级，再发布正式 Release/latest。
3. 同步兼容更新器与防降级逻辑到 dev 后，发布下一个更高底版的 dev.N pre-release。
4. 从已发布附件重新下载验证哈希、文件集合和安装链路；不是仅校验本机构建目录。
5. 后续 main 发布更高正式版时，验证两通道都能正确发现；同底版正式也必须高于其 dev。

回滚：不得通过客户端自动降级撤回坏版本。停止传播有问题的 Release（取消 latest/撤下预览候选，保留事故证据），发布更高修复版本。安装失败的事务性恢复是回滚未完成安装，不等于向用户发布一个低版本更新。更新前备份及结果日志必须保留到确认成功，不能把用户 data 当构建临时目录清理。

## 11. 重点复核、风险与交接完成定义

五个最容易遗漏的输入/失败模式及对应任务：

1. 相同数字程序集版本但不同 dev/正式身份：Task 1、3 验证完整 informational version、commit、hash 和语义顺序。
2. 旧客户端复制旧 helper，拒绝新后缀或新 ZIP 根文件：Task 3、5、7 用真实历史产物验收桥接。
3. 取消/切通道后的迟到异步结果、点击与 timer 同时安装：Task 2、4 验证操作身份、原子 applying 和单 helper。
4. 发行精简后磁盘旧文件残留及用户同目录文件：Task 5 使用精确路径/历史哈希/备份恢复，不递归清空。
5. 共享 DisplayVersion 改后 Windows 构建通过但 Mac 数字版本/签名/promotion 损坏：Task 1、7 验证平台数值与发行渠道分离。

接手 agent 不应重新询问已确认的“模板是否单独下载”“是否保留手动/自动五秒”等问题。若实际源码与基线不同，先报告具体差异并继续不受影响的工作。只有发现真实需求冲突、无法取得必要发布权限或无法安全保存现有用户工作时，才提出针对性问题。

本文的完成定义：用户可审阅一个自包含计划。产品实现完成定义：上述必须验收项有实际证据且无未处理的 P1 问题。Release 完成定义另含授权后的标签、公开附件、远端下载和真实升级检查；三者不能混为一谈。

## 12. 源码与外部依据

- 固定源码基线：https://github.com/Eason4869/deskpet/tree/318a82bf754b349445a08ed0bfc3bc3893e1dbe0
- 更新协调：https://github.com/Eason4869/deskpet/blob/318a82bf754b349445a08ed0bfc3bc3893e1dbe0/src/UpdateCoordinator.cs
- 发行查询：https://github.com/Eason4869/deskpet/blob/318a82bf754b349445a08ed0bfc3bc3893e1dbe0/src/GitHubUpdates.cs
- helper 校验：https://github.com/Eason4869/deskpet/blob/318a82bf754b349445a08ed0bfc3bc3893e1dbe0/src/UpdateInstall.cs
- 打包：https://github.com/Eason4869/deskpet/blob/318a82bf754b349445a08ed0bfc3bc3893e1dbe0/Package.py
- GitHub Releases 官方 API（latest、prerelease、draft、分页及附件 digest）：https://docs.github.com/en/rest/releases/releases

此文档不依赖 Codex memory、特定 agent 会话缓存、私有日志或外部未保存的审查结果。

## 13. 实施阶段追加需求：Live 声纹动效

用户在实施期间追加：bot 稳定输出时已有声纹但无波动，需要优化动效。保持现有 Live 布局、颜色、音频传输和生命周期。使用现有 UI 刷新节奏，以单调时钟驱动轻量形状波动，叠加真实音量；稳定有声时持续变化，静音后回落而非假装仍在说话。PcmOutput 按实际播放的 200ms PCM 分块计算 RMS，避免整段回复共用同一音量。不得另起永久计时器或音频线程。

验证：LiveWaveformTests 用确定时钟和实际位图绘制，覆盖恒定音量两帧不同、静音两帧一致、同一回复不同分块音量不同；运行 LiveTests、DictationTests 及现有语音回归。未接通真实服务/设备时必须明确区分离线绘制验证与真人通话观感。实现和本地验收结果另见同目录 `2026-10-21-update-channels-and-release-cleanup-results.md`。
