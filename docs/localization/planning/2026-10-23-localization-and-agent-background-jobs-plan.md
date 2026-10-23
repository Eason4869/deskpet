# DeskPet 语言 JSON 重构与 Agent 后台任务统一开发计划

> 执行约定：后续获得实施授权后，使用 superpowers:executing-plans 按任务顺序执行，以复选框跟踪进度。本轮仅汇总计划，未实施代码、测试、构建、提交或发布。

**Goal:** 优先完成全应用 `zh_CN.json` 文案重构，再交付 WPF 后台任务，随后立即接入 Windows Avalonia，最终统一验收。

**实施范围更新（2026-10-23）：** 语言阶段已作为 0.10.5-dev.1 合并、仅打 tag；当前目标为 0.10.5-dev.2。用户已接受等效后台设计合同，并授权临时功能分支/草稿 PR 分阶段验证 CI。新增头像菜单语言占位入口，以及下述 S16a 首次引导调整；这些属于后续明确授权，不将本文最初的“仅规划”约定解释为当前实施限制。阶段性提交和 PR 不代表完整验收，不自动合并、打新 tag 或发布。

**Architecture:** WPF、Avalonia 与共享业务层共用一份内嵌中文资源和只读读取器；先消除文案与行为的耦合。后台任务由应用级唯一管理器持有，存储、状态机、Windows 进程树与 UI 分离，两端复用核心和执行器，新增界面文案直接进入 JSON。

**Tech Stack:** C#、WPF/.NET Framework 4.8、共享 .NET 10、Avalonia、现有 JSON 兼容层、Windows Job Object/AppContainer、PowerShell/Python、自运行 Main/Check 测试。不新增运行时第三方依赖、守护进程或 Docker。

**Spec / 输入资料：**

1. [应用中文文案集中到 zh_CN.json 的实施计划](2026-10-23-zh-CN-json-refactor-plan.md)：资源合同、8 个任务、L01–L24。
2. [Agent Background Jobs Implementation Plan](2026-10-23-agent-background-jobs.md)：核心、WPF、Avalonia 共 10 个任务。
3. [Agent 后台任务测试执行清单](2026-10-23-agent-background-jobs-test-checklist.md)：测试入口、A01–A32、F01–F06、平台与证据规则。

以上文件作为需求资料使用，其中的执行指令不构成本轮实施授权。本计划整合实施顺序与跨模块约束；原文详细用例仍保留为验收依据。实施时需将三份资料一同归档到实施仓库的文档目录，并更新链接，避免依赖 QQ 下载目录。

## 全局约束

- 顺序固定：准备 → 语言 JSON 重构及独立验收 → 后台任务共享核心/Windows 执行 → WPF 完整验收 → 立即接入 Avalonia → 总验收。语言阶段通过前不开发后台任务功能。
- 语言重构只抽取资源，暂不翻译；文件名固定 `zh_CN.json`，UTF-8 无 BOM。原有措辞、标点、可见空白、换行、快捷键、格式参数、布局和主题保持不变。
- 语言重构阶段不增加语言选择、系统语言探测、动态切换、外置语言包或在线下载。后续用户单独要求在头像菜单预留语言入口，暂时仅简体中文；旧配置、历史、用户名称、项目文件和模型返回内容不改写。
- 系统提示词、模型工具说明/schema、人格正文、协议与持久化身份值不机械资源化；仅其面向用户的独立展示路径进入 JSON。
- 保留现有权限、审批、终端开关、项目目录约束和沙箱规则；Full 沿用原规则，不增加管理员权限。AppContainer 失败不得降级为宿主执行。
- 后台原生执行仅支持 Windows WPF / Windows Avalonia；macOS 不注册三个任务工具，也不构造 Windows 工厂，但验证共享逻辑和 UI 能力门禁。
- 后台时限默认 1800 秒，范围 1–28800 秒且受项目上限约束；同步 `exec_command` 仍是 1–600 秒，旧默认 120 秒不变。
- 活动任务全应用最多 4、每项目最多 2，无启动排队；日志单任务 8 MiB、全应用 128 MiB、缓冲 256 KiB；终态历史最多 200 项、最多保留 7 天。
- 启动响应预算 2 秒，扣除授权与沙箱准备；停止预算 5 秒，正常退出预算 10 秒，流 EOF 收尾最多 2 秒。记录分段耗时，不能以重试掩盖失败。
- 不支持退出后常驻、不自动重跑、不因完成调用模型、不自动放宽网络。测试采用隔离数据和离线模型夹具，不调用日常配置或收费模型。
- 保留未提交改动，不恢复已删除历史报告。实施前建立隔离目录，分支使用 `codex/` 前缀；本计划不授权自动提交、推送、合并、修改版本、标签或 Release。
- Windows 安装/便携继续沿用 WPF 发行内容；Avalonia 接入不等于增加第二套 Windows 安装器。

## 重点审查风险

1. 显示中文仍参与业务判断：S3 用变更显示标签的测试验证，稳定值和历史兼容值不随资源改变。
2. 大括号、Culture、空白与模板漂移：S2/S6/S7 使用迁移前独立基线，不从迁移后的 JSON 生成预期。
3. 启动交接、落盘失败、根进程先退出：S10/S11 用屏障与真实进程树证明副作用和所有权边界。
4. 同数据目录多实例、恢复/退出/删除竞争：S9/S10/S13/S15 在管理器锁和独占租约内处理，不能只依赖 UI 检查。
5. 双端会话归属、日志 JSON 截断、历史污染：S12/S15/S16 验证原会话绑定、最大页完整 JSON 与旧文件散列。

## 1. 合并决策与资料缺口

### 1.1 实施顺序与阶段门槛

| 阶段 | 任务 | 可独立审查的产物 | 下一阶段条件 |
| --- | --- | --- | --- |
| P0 准备 | S0 | 实施基线、环境、资料与缺口清单 | 语言阶段输入和工作区明确 |
| P1 语言 JSON 重构 | S1–S7 | 唯一资源、双端完整迁移、审计、成品验证、L01–L24 | 所有语言必需项通过；未覆盖平台明确列出并补齐所需证据 |
| P2 后台核心与原生执行 | S8–S12 | 合同、存储、状态机、真实 Windows 执行、三个工具 | 核心、原生和工具集成测试通过 |
| P3 WPF 后台任务 | S13–S14 | 应用服务、项目门禁、退出、任务卡和面板 | WPF 适用 P0 验收全部通过并保存证据 |
| P4 Avalonia 后台任务 | S15–S16 | 最小持久绑定、Windows 真执行、UI、退出、Mac 门禁 | Avalonia 适用 P0 验收全部通过 |
| P5 总验收 | S17 | 双端、双运行时、平台证据和维护文档 | 所有必需项满足，P1 结果单列 |

这里的 P0–P5 是开发阶段编号；验收中的“P0/P1”沿用原文的缺陷优先级，二者含义不同。

### 1.2 跨阶段统一规则

- S8 起新增 `agent.jobs.*`、后台设置、审批展示、错误和退出提示等用户文案，必须直接调用 `TextResources`，与调用点同批增加资源及清单记录。
- 状态枚举、工具名、JSON 字段、原因码、持久化字段保持稳定英文协议值；UI 映射到资源。日志正文、命令输出、模型内容作为数据原样显示。
- 工具 schema 的模型指令继续作为模型合同；审批 UI 的中文展示单独资源化，不将整段 schema 当语言包。
- 两阶段都可能修改 `Settings.cs`、`WorkSettingsPanel.cs`、聊天窗口、Build、测试入口和 CI，必须顺序集成；后台功能从语言验收后的基线开始，不能重新引入硬编码。
- 语言阶段的文案基线冻结既有文案；后台新增文案作为“新功能新增”登记，不拿它覆盖旧基线或误判为重构文案变化。
- 测试目标随功能任务登记，S7/S17 做总审计；不能把登记全部拖到最后，导致前序任务无法验证。

### 1.3 必须补齐的设计资料

后台计划与测试清单共同引用 `../specs/2026-10-23-agent-background-jobs-design.md`，该文件未包含在本次三份输入中，在下载目录也未找到同名文件。

因此本计划保留已给出的接口、限额和验收，但不声称掌握三个工具的完整 schema、返回字段、错误结构与幂等键组成。**这不阻挡 S1–S7；S8 开始前必须取得该设计，或明确审定一份等效合同。** 不自行猜测并实现缺失协议。补齐后将合同纳入实施仓库，逐项与 A01–A32 核对。

### 1.4 基线状态

源计划引用的核对基线为 `03170c127d90abdab29ddae2bdf5912027d4055c`。语言计划的较早统计基线是 `7f2e0c38fddd802ac00f6807f4896d1034753424`。

本轮只读检查发现 `E:/workspace/deskpet/source` 为 `594363af7c2cf313ea11ff5890c0fab8d42d8c26`，还有两份未跟踪计划文件；外层另有多个不同提交的副本。因此该目录不能直接作为上述计划的最新实施基线。本轮未联网核对远端，也未切换任何分支。S0 重新确认目标仓库、最新目标分支、完整 SHA 和 dirty 清单。

## 2. 文件职责与稳定合同

以下均为未来实施仓库的相对路径，本计划当前保存在外层工作区文档目录。

| 范围 | 新增/修改文件 | 职责 |
| --- | --- | --- |
| 语言资源 | `assets/locales/zh_CN.json`、`src/TextResources.cs` | 唯一扁平字符串目录、一次加载、只读 Get/Format |
| 语言审计 | `tools/verify_localization.py`、`docs/localization/migration-inventory.jsonl`、`tests/fixtures/localization/text-baseline.json` | 候选分类、排除理由、模板和迁移前基线 |
| 语言测试 | `tests/LocalizationTests.cs`、`tests/LocalizationUiTests.cs`、`desktop/DeskPet.Desktop.Tests/LocalizationUiChecks.cs` | 共享合同与双端 UI |
| 后台共享核心 | `src/AgentKernel/AgentJob.cs`、`AgentJobPolicy.cs`、`AgentJobStore.cs`、`AgentJobLogStore.cs`、`AgentJobManager.cs`、`AgentJobTools.cs` | 合同、策略、持久化、日志、状态机、工具 |
| 原生执行 | `src/WindowsAgentJobProcess.cs` | 挂起创建、Job Object、流、沙箱/目录租约 |
| 宿主/归属 | `src/ConversationAgentJobs.cs`、`PetAgentJobServices.cs`、`AgentJobCatalogGuard.cs` | 可信 Owner 注入、应用服务与目录变更门禁 |
| WPF 任务 UI | `src/AgentJobViewState.cs`、`AgentJobCard.cs`、`AgentJobsPanel.cs`、`ConversationAgentJobCards.cs` | 可共享视图状态、WPF 卡片/面板、原消息定位 |
| Avalonia 绑定/UI | `desktop/DeskPet.Desktop/DesktopWorkspaces.cs`、`DesktopWorkSessionBinding.cs`、`WorkProjectPicker.cs`、`AgentJobCard.cs`、`AgentJobsPanel.cs` | 最小项目绑定和双端一致能力 |
| 后台测试 | `tests/AgentJob*Tests.cs`、`AgentJobTestSupport.cs`、`AgentJobRegistrationTests.py`、Avalonia 三组 `AgentJob*Checks.cs` | 原清单的逻辑、原生、UI、退出和登记测试 |
| 构建/CI | `Build.ps1`、Core/Tests/Desktop.Tests 工程、`PortableTests.cs`、UI `Program.cs`、`check.yml`、`macos.yml` | 共享源、内嵌资源、默认/专用入口及失败附件 |
| 文档 | `docs/localization/zh-CN-resource-guide.md`、`zh-CN-migration-validation.md`、`docs/agent-background-jobs.md`、`docs/agent-background-jobs-validation.md` | 资源维护、使用说明和分阶段验收 |

### 2.1 语言 API

固定逻辑资源名：`Dafeiyu.Locales.zh_CN.json`，从定义 `TextResources` 的程序集读取。共享核心与各独立测试编译产物分别正确登记资源，不依赖当前目录或入口程序集。

```csharp
TextCatalog.Load(Stream stream) // 调用方管理 stream 生命周期
TextCatalog.Get(string key)
TextCatalog.Format(string key, params object[] args)
TextCatalog.Format(IFormatProvider provider, string key, params object[] args)
TextResources.Get(string key)
TextResources.Format(string key, params object[] args)
TextResources.Format(IFormatProvider provider, string key, params object[] args)
```

精确大小写 Key；未知 Key 返回 `[key]` 并记录一次诊断。目录整体缺失/损坏抛出可定位 `InvalidDataException`，由既有启动错误处理承接；唯一允许的独立中文兜底为“界面文字资源不可用，请修复安装。”并登记排除。格式参数或模板错误不吞异常。默认使用 CurrentCulture，原显式 InvariantCulture 等行为继续保留。目录不暴露可变 Dictionary，初始化一次且线程安全。

Key 使用稳定英文功能路径，值全部为字符串；完整句子使用编号模板，Get 不转义普通大括号，Format 才使用 `{{`/`}}`。不强制合并不同上下文的同文词条。

### 2.2 后台 API 与身份

共享源码 namespace `Dafeiyu`，兼容现有 Framework 编译器。`AgentKernel/*.cs` 按现有通配收录；src 根共享文件显式加入 Core，WPF UI 不加入 Core。

```csharp
enum AgentJobState { Starting, Running, Stopping, CleanupFailed,
    Succeeded, Failed, Stopped, TimedOut, StartFailed, Interrupted }
enum AgentJobStopReason { User, Timeout, ApplicationExit, OwnerDeleted, LogStorageFailed }

// Manager 的公开异步合同
Task<AgentJobSnapshot> StartAsync(AgentJobStartContext context,
    AgentJobSpec spec, CancellationToken requestToken);
Task<AgentJobPage> GetAsync(AgentJobOwner owner, string id, string cursor,
    int limitBytes, CancellationToken token);
Task<AgentJobSnapshot> StopAsync(AgentJobOwner owner, string id,
    AgentJobStopReason reason, CancellationToken token);
Task RecoverAsync(CancellationToken token);
Task<AgentJobExitReport> StopAllForExitAsync(CancellationToken token);
```

Owner 为宿主注入且不可变的 `projectId + conversationId + agentId`；主助手 AgentId 固定 `primary`，不取瞬态 kernelSession/profile ID；只读子 worker 不继承启动能力。请求不得提供原生 PID/句柄。UI 内部 `ListForProject` / `StopForUserAsync` 不映射到模型工具。

原生工厂 `PrepareAsync(spec, policy, output, token)` 返回已入 Job 的挂起进程，进程接口提供 `Resume()`、`Completion`、`StopAsync(token)`、`Dispose()`。Completion 表示整树与限时流收尾完成，主进程退出不等于任务终态。

## 3. 逐项实施任务

每个行为任务遵循：先写有业务判据的失败测试 → 确认失败原因 → 最小实现 → 运行相关新旧测试 → 更新清单与证据。每个任务形成可独立审查的改动批次；是否提交与推送以实施时授权为准。

### S0：准备实施基线与资料

**文件：** 实施资料归档、本计划、环境/基线记录。**产出：** 目标仓库、SHA、工作区与缺口清单。

- [ ] 读取适用 AGENTS.md，确认目标分支、远端 SHA、未提交改动和其他任务占用；建立隔离实施目录。
- [ ] 归档三份输入，核对遗漏设计资料；为后台完整合同建立 S8 前置门槛。
- [ ] 确认 Framework64 csc、Windows PowerShell 5.1、WebView2、Python，以及实际 `desktop/global.json`；源计划要求 SDK 10.0.402/latestPatch，发现变化记录差异。
- [ ] 记录相关已有测试的基线结果；使用独立数据目录，协调原生 UI 焦点，不修改防火墙或 AppContainer 豁免。

### S1：语言候选清点与迁移前基线

**文件：** `verify_localization.py`、迁移清单、`text-baseline.json`。**接口：** `inventory --baseline <sha>`、`check --phase inventory`。**对应：** 原语言任务 1；L01/L16。

- [ ] 扫描普通、转义、逐字、插值字符串并排除注释；不支持语法显式列入人工复核。夹具覆盖中文转义、大括号和同文不同用途。
- [ ] 补查纯英文固定 UI 文案、间接来源、Tooltip/可访问名称、错误与空状态；不只搜索汉字。
- [ ] 每条记录 baseline_sha/file/context/literal_hash/occurrence/kind/decision/key 或 exclusion_reason/reviewed；全部 migrate/exclude，未分类为 0。
- [ ] 从迁移前提交采集独立文案基线，列出全部文字参与比较/筛选/状态判断的位置；人工复核排除项。

### S2：资源读取器与双运行时构建

**文件：** `TextResources.cs`、JSON、LocalizationTests、Build/Core/Tests 工程与 PortableTests。**产出：** §2.1 API 与内嵌资源。**对应：** 原语言任务 2；L02–L07/L19。

- [ ] 先验证中文、引号、反斜杠、换行、普通大括号、缺 Key/资源、损坏 JSON、非字符串值、格式参数不足及并发读取的失败用例。
- [ ] 复用现有 JavaScriptSerializer/.NET 10 兼容层，实现一次加载；严格重复 Key 检查由 Python 构建门禁承担，不重写通用解析器。
- [ ] 登记 `LocalizationTests`、`--localization-only` 与默认入口，补齐 App 和受影响独立测试资源嵌入。
- [ ] 从源码目录外测试，10,000 次并发读取正确且仅解析一次；Get 与 Format 的大括号/空值/格式提供者分别验证。

### S3：解除显示文字与行为耦合

**文件：** AboutPanel、ImageGenerationSettings、ConnectionSettingsPanel、ReplyFiles、ShellMenus/ChatAppShell、BrowserPane、ProjectSidebar、Settings、Avalonia OfficialCompanionProgressView 及 S1 新发现位置。**对应：** 原语言任务 3；L08–L10。

- [ ] 先以改变显示标签验证颜色、默认参数省略、朗读成功、菜单动作/分组、下载错误优先级与对话框用途仍正确。
- [ ] 使用局部状态/动作/稳定身份替代文字推断；不能改成比较 `TextResources.Get(...)`。
- [ ] 旧中文默认名称及自定义名称加载/保存往返不变；不把资源 Key 写入身份字段。
- [ ] 审核剩余中文比较/Contains/StartsWith，每处确认属于兼容值或真实内容处理。

### S4：WPF 文案分批迁移

**文件：** S1 清单中的 src UI；新增 `LocalizationUiTests.cs`。**对应：** 原语言任务 4；L11/L18/L21。

- [ ] 按设置/引导 → 聊天/项目 → 图像/任务 → 角色/关于迁移，每批同步 JSON、调用点、清单。
- [ ] 固定句 Get、动态整句 Format；处理 const/默认参数编译约束，不变控件 Name、动作、布局、主题或加载顺序。
- [ ] 每批保留独立中文显示断言并运行对应既有测试；确认框、错误、空状态、Tooltip/辅助文字一并覆盖。
- [ ] WPF 范围待迁移数为 0，浅深主题与 390 DIP/宽窗对照通过。

### S5：Avalonia 文案分批迁移

**文件：** desktop 生产 UI 与 `LocalizationUiChecks.cs`、UI Program/工程。**对应：** 原语言任务 5；L12/L18/L21。

- [ ] 按设置/引导 → 聊天/项目 → 图像/角色迁移；同含义复用 Key，既有不同措辞保留不同词条。
- [ ] 使用同一读取器和源 JSON，不复制资源；保持 Placeholder、快捷键、辅助名称及布局。
- [ ] 专用 `--localization-ui-only` 与默认 UI 入口均登记；验证主题、390 DIP、错误状态、页面关闭重开。
- [ ] Avalonia 范围待迁移为 0，相关显示与行为回归通过。

### S6：共享提示、动态模板和历史兼容

**文件：** 清单中的图像、存储、下载/更新/导入、会话和权限提示。**对应：** 原语言任务 6；L04/L05/L10/L13–L16。

- [ ] 先保留动态表达式独立预期，覆盖空值、0/1/多项、中文路径、引号、换行和大括号。
- [ ] 抽取完整模板，保持错误类型、堆栈/内部异常、阶段、取消、重试与 HTTP 行为。
- [ ] 在 zh-CN/en-US/de-DE 下比较迁移前后数字/日期格式；原显式 InvariantCulture 保持一致。
- [ ] 旧 Error/名称和历史原样可读，新记录仍保存最终中文；模型提示词/schema 排除路径与基线相同。

### S7：语言审计、成品验证与阶段验收

**文件：** 审计脚本、构建/CI、语言维护和验证文档。**对应：** 原语言任务 7–8；L17–L24 及全部 L 项收口。

- [ ] `check --phase complete` 检查 UTF-8 无 BOM、重复/空白 Key、类型、模板、引用、未解释闲置 Key、未分类和未迁移项。
- [ ] 每个排除项匹配具体上下文/摘要，不允许整目录放行；保留措辞、标点、空白、换行基线比较。
- [ ] 检查 App/独立测试/Core/Avalonia 实际产物中资源名与内容；便携、精简、安装器所含应用离开源码和外置 JSON 后可读取。
- [ ] 运行资源专用、默认共享、默认 Avalonia、受影响旧功能和双端真实 UI；Windows/macOS Intel/Apple Silicon 分开记录。
- [ ] 完成 L01–L24，形成语言阶段基线与维护规范；未运行/环境阻断如实记录，不能宣布阶段全部通过。
- [ ] 语言阶段门槛通过后才进入 S8；缺少远端执行授权时保留相应验收缺口，不为取 CI 自动推送。

### S8：后台合同、设置及工具协议冻结

**文件：** AgentJob/Policy、Settings、ProjectCatalog、ConversationWorkspace、WorkSettingsPanel、ContractTests/TestSupport、构建入口、JSON。**对应：** 原后台任务 1；F04。

- [ ] 先补齐并审定缺失设计合同；记录三个工具的精确参数/返回、幂等键、错误、分页边界和策略。
- [ ] 写 LegacySettingsKeepExecTimeout、LegacyProjectDefaultsTo1800、CopyAndEffectiveSettingsKeepBackgroundLimit、ValidateExactArgumentBounds；验证 0/28801 拒绝、1/28800 边界。
- [ ] 实现新时限字段默认值、Normalize/Copy/EffectiveSettings/保存重开；旧同步默认 120、上限 600 不变。
- [ ] 登记合同专用与默认入口，运行 WorkSettingsTests/ProjectCatalogTests；新增显示词条全部进 JSON。

### S9：元数据、日志、配额与恢复

**文件：** AgentJobStore/LogStore、StoreTests/LogTests。**接口：** SaveAsync/LoadAsync；Append/ReadAsync/FlushAsync。**对应：** 原后台任务 2；A15–A19/A23/F01。

- [ ] 先测试原子替换失败、重解析点/非法 jobId、第二 writer、半尾/损坏隔离与恢复非终态为 Interrupted；恢复不启动工厂、不附着或操作旧 PID。
- [ ] 实现同目录原子写、版本、64 KiB 元数据记录上限与独占目录租约；锁顺序明确，管理器锁不等待磁盘。
- [ ] 日志最多 2×4 MiB 段（含封装），单记录正文 UTF-8 ≤8 KiB，队列 ≤256 KiB；正文页按 UTF-8 字节计数。
- [ ] 1024 字节最小页可在长记录内推进，片段有 fragment_offset；游标绑定 job/seq/片段/代次，过期/轮转/丢弃明确 gap，不能接受路径或裸偏移。
- [ ] 用逐字节中文、stdout/stderr、无换行、≥20 MiB 洪泛和慢写入器验证上限/无死锁；用假时钟验证 200 项/7 天（按 FinishedUtc）及 128 MiB 总配额和活动任务 8 MiB 预留。

### S10：管理器、幂等与启动交接

**文件：** AgentJobManager/ManagerTests、受控假进程/屏障/授权 spy。**产出：** §2.2 Manager API、Changed 快照通知。**对应：** 原后台任务 3；A05/A10/A12–A14/A18/A20/F02。

- [ ] 先验证 TenConcurrentStartsLaunchOnce、SameKeyChangedPayloadDenied、CapacityRecheckedAfterApproval、WorkspaceDisposeDoesNotStopCommittedJob。
- [ ] 按规范化/授权 → 权限及目录身份复核 → 预留并持久 Starting → 挂起准备 → 取消检查 → Resume/持久 Running → 原子交接实现；副作用前登记 beforeEffect。
- [ ] 用屏障覆盖审批、准备、Resume、Running 落盘、响应各处取消；交接前清理、交接后独立于请求 token，失败不自动重跑。
- [ ] 幂等先查原记录再检查容量；旧/丢失记录由宿主执行日志阻止重跑，不把短期 TryClaim 当永久保证。
- [ ] 清理确认才释放活动名额；CleanupFailed 占名额，根进程退出但子树存活仍 Running；停止/自然退出竞争仅一个终态通知。
- [ ] 测试 Starting 写失败无进程、Running 写失败杀树并报告可能已执行、log_storage_failed、恢复中和退出后拒绝启动。

### S11：真实 Windows Job 与 AppContainer

**文件：** WindowsAgentJobProcess、Core 显式收录、ProcessTests/SandboxTests/TestSupport。**对应：** 原后台任务 4；A01/A04/A09–A12/A22/A25/A31/F06。

- [ ] 建立 wait/exit-code/utf8-fragments/flood/tree/root-exit-child/http-server fixture；随机 nonce 就绪握手、中文空格临时路径、系统分配回环端口，不用 Sleep 猜就绪。
- [ ] 先验证挂起分配失败/取消无副作用、整树停止且同名无关进程存活、根先退出、Job 句柄不继承、EOF 收尾、实际退出码。
- [ ] 实现挂起创建 → kill-on-close Job 分配 → 异步排流 → Resume；保留沙箱和授权目录租约至整树结束。
- [ ] 复用当前 PowerShellBootstrap 和干净环境白名单，不退回旧实现；验证 AppContainer 主/子进程越界失败、准备失败无宿主 fallback。
- [ ] Framework 与 .NET 10 Windows 分别运行真实 native 测试；100 次短任务后自有进程/名额/句柄归零，A31 作为 P1 单列结果。

### S12：工具、权限和跨轮会话归属

**文件：** AgentJobTools、ConversationAgentJobs、Workspace/Catalog/Permission/AccessPolicy、会话绑定、ToolTests/IntegrationTests。**对应：** 原后台任务 5；A03/A05–A08/A13/A29/F03。

- [ ] 宿主 `ConfigureJobs(manager, owner)` 冻结可信身份；拒绝普通聊天/Unassigned/Quick/Draft，忽略或拒绝伪造 Owner，子 worker 不继承启动能力。
- [ ] schema 与直调都验权限：Plan/关闭终端不启动，Auto 沙箱 shell 仍人工审批，Full 沿用规则；冻结 payload，审批后再验参数/策略/目录身份，启动只审批一次。
- [ ] start_job 按写/执行策略，get_job 为 ReadOnly 且新调用读实时状态；最大 64 KiB 日志页不被 24000 字符写 journal 截断，始终返回完整 JSON。
- [ ] get/stop 跨项目/会话/agent 与未知 ID 返回相同错误；关闭终端后仍保留可信任务停止路径。
- [ ] 脚本模型驱动真实会话工具路径验证跨轮 Dispose 后继续、丢响应不重跑、build→plan 后仍能 get、完成零新增模型调用；假 Manager 测试不冒充集成。

### S13：WPF 服务、项目门禁和退出

**文件：** PetAgentJobServices、AgentJobCatalogGuard、PetWindow/PetImageServices、项目/会话操作、LifecycleTests/ProjectTests、JSON。**接口：** `RunOwnerMutationAsync(projectId, conversationId, mutation, token)`。**对应：** 原后台任务 6；A21–A24/F01/F02。

- [ ] 唯一应用管理器异步恢复，租约失败仅限制任务与关联绑定写入，普通聊天继续；退出屏障拒绝新启动和待审批。
- [ ] 删除前同一门禁作用域内阻止启动并确认清理，失败不改目录/模型；移动/改根有活动任务则拒绝，覆盖未打开会话和并发启动。
- [ ] 隐藏窗口、归档与关面板不取消任务；UI 不同步等待清理。
- [ ] 独立宿主分别验证 normal/tray/update/kill/cleanup-timeout，≤10 秒正常退出；清理未确认保留窗口和重试，强杀后树回收、恢复 Interrupted 不重跑。
- [ ] 回归图像任务退出、更新、项目/会话归档删除，不绕过已有清理服务。

### S14：WPF 卡片、项目任务面板与阶段验收

**文件：** AgentJobCard/JobsPanel/ViewState、ConversationAgentJobCards、聊天/工作区/导航、UiTests、JSON。**对应：** 原后台任务 7；A02/A04/A26–A29/A32。

- [ ] 卡片绑定原 Owner/调用/回复；项目面板筛选排序，已归档会话有入口；关闭卸载释放订阅，任务继续，刷新 ≤4 Hz。
- [ ] 状态/错误/时限提示使用 JSON；纯文本显示命令日志、ANSI/HTML/伪工具指令，不触发执行或模型调用；终态最多一次通知。
- [ ] URL 仅回环候选，最多 5 项，移除凭据和 query；点击前 HTTP 计数为 0，不声称 Ready；点击后真实浏览器读取 fixture 正文，停止释放端口。
- [ ] 验证浅深/390 DIP/宽窗、不抢滚动、隐藏重开；长任务时交互 ≤500 ms、30 秒任务 start 响应 ≤2 秒。
- [ ] WPF 适用 A01–A29/A32 全部 P0 通过，A30 记录尚未完成 Avalonia，A31 单列；保存 Framework/.NET 10 与 WPF 证据，随后进入 S15。

### S15：Avalonia 最小持久项目/会话绑定

**文件：** DesktopWorkspaces、DesktopWorkSessionBinding、WorkProjectPicker、Controller/ChatWindow/WorkSettingsView、BindingUiChecks、JSON。**对应：** 原后台任务 8；A08/A24/A30/F05。

- [ ] 复用 projects.json、ProjectCatalogStore、ConversationWorkspaceManager 和 guard；OpenAsync 返回稳定项目/会话 Session/Queue/EffectiveSettings，不创建临时身份或搬迁普通聊天历史。
- [ ] 增加最小选择/新建及必要管理菜单，不搬整个 WPF 拖动侧栏；不同窗口同持久会话复用 Workspace，避免双 writer。
- [ ] 发送/停止、权限/模型、草稿、听写、ask_user、图片、清空/导出全部使用本窗口绑定；未绑定继续原普通 Session。
- [ ] 审批展示完整来源/payload/目录/沙箱/时限，切项目不串答；普通历史散列不变，绑定失败保留旧窗口与文件。
- [ ] 专用及默认入口验证重开 Owner 稳定、双宿主租约、队列/问题/图片/听写隔离。

### S16：Avalonia 任务 UI、Windows 执行与退出

**文件：** Avalonia JobCard/JobsPanel、Controller/ChatWindow/WorkSettingsView、UiChecks/ExitChecks、JSON。**对应：** 原后台任务 9；Avalonia 适用全部 A 项。

- [ ] Windows 所有已绑定工作会话注入同一管理器/原生工厂；macOS 不创建工厂、不注册工具，伪造直调也拒绝；关窗口不结束任务。
- [ ] 复用 ViewState、候选和通知逻辑，保持原会话卡片、≤4 Hz、主题/窄窗/滚动与资源文案检查。
- [ ] .NET 10 Windows 真进程验证工具/日志/停止/预览，不能复用 WPF 截图代替；保留 ImageTasks、Live、Dictation 退出清理。
- [ ] normal-exit/held-exit/kill-host 各用独立进程；正常收尾、超时保留与强杀后恢复均保留证据。
- [ ] 验证 Mac Intel/Apple Silicon 共享与门禁；默认 Avalonia 非退出 UI、专用退出和 Windows native 全部适用 P0 通过。

### S16a：首次引导前置 GitHub 加速、角色选择与全量包准备（新增）

**需求来源：** 用户于 2026-10-23 明确要求纳入当前计划，目标随 0.10.5-dev.2。此项在后台任务双端接入后、S17 总验收前完成，不替代原 A/F 验收。

**文件：** WPF OnboardingLessons/OnboardingWindow/OnboardingUpgrade，Avalonia 现有 GuideWindow/首次启动宿主，OfficialCompanionDownloads 及应用级下载服务、中文 JSON 与独立文案基线；新增引导准备测试并回归现有角色恢复和下载去重测试。

- [ ] 首次引导的设置操作依次为：GitHub 下载加速 → 角色选择 → 强烈推荐“下载并更新全量官方角色包” → 其余模型/互动/工作引导。欢迎页可保留；网络路由复用现有 GitHub 加速设置并立即保存，不新增全局代理或未经选择的加速地址。
- [ ] 推荐卡清楚说明全量素材的用途、下载体积（有元数据时显示）和后台执行；提供突出主按钮及“稍后下载”。用户明确点击后开始全量检查、下载缺失包及更新旧包，跳过不阻塞继续引导。
- [ ] 复用现有 EnsureAllAsync/角色下载协调器、校验及原子安装；下载归应用生命周期所有，切换步骤、完成或关闭引导不取消。全量下载与单角色选择共享传输/安装，不重复启动批次；此下载不使用 Agent 的命令后台任务工具。
- [ ] 下载启动后立即可继续后续引导，显示简要后台进度及可返回的进度入口。当前有效旧角色继续显示并更新后热加载；未安装角色准备期间保持当前可用角色，完成后按最后一次选择加载，不回跳引导。主界面头像及设置页切换角色仍保留既有弹窗提示。
- [ ] 离线、取消、部分失败及路由变更有明确状态/重试入口，不阻塞后续引导、不丢已装包、不重置当前角色；已是最新的角色复用本地安装。沿用现有退出清理，不承诺应用退出后继续下载。
- [ ] 保留旧 lesson ID、已完成/延后状态及手动重开入口；升级用户不被强制重新引导，不新增强制全量下载。WPF 与 Avalonia 复用相同下载服务语义，不移植整套 WPF 引导。
- [ ] 所有新增可见文案进入中文 JSON，暂不翻译。补齐 O01–O08 测试和双端 UI 证据后交给 S17 收口。

### S17：统一登记、总回归与交付

**文件：** Build/工程/默认入口/CI、AgentJobRegistrationTests.py、两阶段维护及验收文档、README、agent-workflows/conversation-agent、runtime_package.py 文档清单。**对应：** 原后台任务 10 + 两阶段总收口。

- [ ] 静态登记检查覆盖 ValidateSet、辅助源、共享默认/专用、原生入口、双端 UI、独立退出、CI 失败附件；静态检查不替代行为测试。
- [ ] 顺序完成受影响检查后，再运行一次默认共享和默认 Avalonia；新增修改只补相关范围，避免无依据反复全量。
- [ ] 从语言验收基线复核新增后台文案全部资源化，旧文案/模型提示未漂移；重新运行 localization complete 门禁。
- [ ] 输出 L01–L24、A01–A32、F01–F06 逐项状态及平台，区分本机/远端、假进程/原生、headless/真实浏览器；未运行不填通过。
- [ ] 新增引导 O01–O08：顺序正确；加速设置用于首次下载；缺包/旧包/最新包路线正确；后台下载不阻塞后续引导；关闭引导仍下载；重复点击/并发角色选择去重；失败/取消/稍后下载可继续并能重试；旧用户引导状态与主界面角色弹窗保持兼容。分别登记 WPF/Avalonia 适用结果。
- [ ] 清理自有 fixture/临时进程，汇总代码与资源统计、最终 SHA、剩余问题；源码交付与推送/合并/发版分别报告。

## 4. 验收追踪与测试入口

### 4.1 语言验收映射

| 验收 ID | 统一任务 |
| --- | --- |
| L01 | S1 |
| L02–L07 | S2/S6/S7 |
| L08–L10 | S3/S6 |
| L11/L12 | S4/S5 |
| L13–L16 | S1/S6 |
| L17–L20 | S2/S7 |
| L21–L24 | S4/S5/S7，S17 再审计新功能 |

语言阶段全部 L 项是必需项，包含成品实际资源验证，不能只统计 JSON 词条数。扫描粗统计原有 260 文件/4866 次只是历史候选数，不是新基线或完成率。

### 4.2 后台验收映射

| ID | 关键通过判据 | 任务 |
| --- | --- | --- |
| A01 | 30 秒任务仍存活，启动预算 ≤2 秒，阶段耗时完整 | S11/S14/S16 |
| A02 | 长任务中第二轮/切会话/日志读取成功，固定交互 ≤500 ms | S14/S16 |
| A03 | 第一轮 Workspace Dispose 后进程继续，第二轮同 Owner 查询 | S12 |
| A04 | 区分 exit0/7，2 秒内可见，并记录根/整树完成时间 | S11/S14/S16 |
| A05 | 10 并发仅一次副作用，参数冲突拒绝，丢响应/重开不重跑 | S10/S12 |
| A06 | Plan/禁用/非项目/Quick 的 schema 与直调均拒绝启动 | S12/S16 |
| A07 | 审批冻结且仅一次，变参数/策略/目录身份不能复用授权 | S10/S12 |
| A08 | 跨 Owner 与未知任务同错误，UI 仅可信归属 | S12/S15 |
| A09 | 5 秒内父子孙全停，同名无关进程仍存活 | S11 |
| A10 | 根退出、子树存活仍 Running，保留 root_exit_code | S10/S11 |
| A11 | 挂起取消/分配失败无副作用，句柄/名额归零 | S11 |
| A12 | 3 秒超时为 TimedOut，停止/自然退出只一个终态 | S10/S11 |
| A13 | 每个交接屏障取消，交接前回收、交接后保留 | S10/S12 |
| A14 | Starting 持久失败不启动，Running 持久失败清树且不重跑 | S9/S10 |
| A15 | 中文逐字节、双流、长行，最小页推进且准确拼接 | S9 |
| A16 | ≥20 MiB 输出仍满足 8 MiB/256 KiB，有 gap、不死锁 | S9 |
| A17 | 路径穿越/重解析点/跨 job 或错代游标全部拒绝 | S9 |
| A18 | 应用第5/项目第3任务不启动，清理失败保留名额 | S10 |
| A19 | 7 天/200 项/128 MiB 边界，按完成时间，活动预留受保护 | S9 |
| A20 | 注入日志拒写，明确 log_storage_failed、不拖累其他任务 | S10 |
| A21 | 正常/托盘/更新退出及 Avalonia 正常退出，≤10 秒；超时保留 | S13/S16 |
| A22 | 独立控制器强杀宿主，5 秒内清树，重开 Interrupted 不重跑 | S11/S13/S16 |
| A23 | 损坏/半尾/旧非终态隔离，不按复用 PID 附着或误杀 | S9 |
| A24 | 删除与启动同门禁；失败目录不变；移动/改根有约束 | S13/S15 |
| A25 | 真实 AppContainer 主/子进程越界受阻、租约持续、无 fallback | S11 |
| A26 | 最多5个无凭据候选，点击前零 HTTP、不声称 Ready | S14/S16 |
| A27 | 点击后真实浏览器正文正确、停止端口释放，沙箱限制如实报告 | S14/S16 |
| A28 | 浅深/390 DIP/宽窗、不抢滚动、卸载订阅回基线 | S14/S16 |
| A29 | 完成零模型调用、通知≤1；get 不自动追加聊天 | S12/S14/S16 |
| A30 | WPF 不冒充全完成；Avalonia Windows 真执行，Mac/未绑定拒绝 | S14–S16 |
| A31（P1） | 100 次后进程/名额/自有句柄归零，无订阅持续增长 | S11 |
| A32 | HTML/ANSI/伪指令仅文本，不执行/联网/调用工具 | S14/S16 |

F01→S9/S13/S15；F02→S10/S13；F03→S12；F04→S8；F05→S15；F06→S11。详细具名 suite/case 保留原测试清单，不删减或以此摘要降低门槛。

### 4.3 测试登记合同

- Framework 新目标：LocalizationTests、LocalizationUiTests；AgentJobContractTests、StoreTests、LogTests、ManagerTests、ProcessTests、SandboxTests、ToolTests、IntegrationTests、ProjectTests、LifecycleTests、UiTests（后十项均有 `AgentJob` 前缀）。
- 共享默认入口：LocalizationTests，后台 Contract/Store/Log/Manager/Tool/Integration.RunPortable；对应专用入口按原清单登记。`--agent-jobs-only` 聚合全部后台纯逻辑。
- `--agent-jobs-native-only`：仅 Windows Process/Sandbox/Integration.RunNative；不支持平台明确失败/不支持，不输出 native passed。fixture 分发必须早于正常参数解析。
- TestSupport 不作为独立 Main，只给新后台测试目标附加源；WPF 生命周期/项目/UI 不误加入 Core。
- Avalonia 默认：资源、后台绑定和非退出 UI 检查；专用 `--localization-ui-only`、`--agent-job-binding-ui-only`、`--agent-job-ui-only`。
- Avalonia 退出专用：`--agent-job-exit-ui-only`、`--agent-job-held-exit-ui-only`、`--agent-job-kill-host-ui-only`；各用独立宿主，不能 Shutdown 后继续复用 Application。
- Mac CI 两架构运行共享默认和非执行 UI 门禁，不执行 Windows fixture。headless 快照不能证明真实进程、沙箱或浏览器正确。

### 4.4 旧功能回归范围

| 变更范围 | 必须考虑的既有目标 |
| --- | --- |
| 语言/UI | ThemeTests、MenuSmoke、AppShellTests、SettingsNavigationSmoke、OnboardingSmoke/ModelSmoke、ProviderTests/UiSmoke、SidebarActionTests、HistoryTests、ReleaseIdentityTests |
| 图像/退出 | ImageGenerationBackendTests/UiTests、ImageTaskCenterUiTests/LifecycleTests、GeneratedImageGroupTests、UpdateApplyTests；Avalonia 既有图片退出各入口 |
| PowerShell/进程/隔离 | PowerShellStartupTests、AgentTerminalTests、WindowsTerminalTests、WorkspaceSandboxTests、AgentIsolationTests、McpConnectionTests |
| 工具/权限 | AgentApprovalTests、WorkspacePermissionTests、AgentRunRecoveryTests、AgentCodingWorkflowTests、AgentKernelIntegrationTests |
| 项目/会话 | WorkSettingsTests、ProjectCatalogTests、ConversationWorkspaceManagerTests、ConversationOwnershipTests、ConversationArchiveRegressionTests、SidebarProjectInteractionTests |
| 工作区/UI/预览 | UnifiedWorkspaceTests、UnifiedPreviewTabsTests、ConversationViewportTests、BrowserPaneTests、TerminalUiTests；若触及公共终端再补 McpUiSmoke |

执行前按实际基线核对目标是否存在；被改模块的其他已有测试同样纳入。原生桌面测试串行，基线失败/环境阻断保留证据，不删断言、不无上限增加超时。

## 5. 未来执行命令与证据

以下为未来登记完成后的命令合同，本轮均未执行。每条单独检查退出码，失败停止该轮并保存日志，不能只检查最后一条结果。

```powershell
# 语言阶段：实施仓库根
python tools/verify_localization.py inventory --baseline <sha>
python tools/verify_localization.py check --phase inventory
python tools/verify_localization.py check --phase complete --report work/localization-audit.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationUiTests

# .NET 构建：desktop/global.json 生效目录
Push-Location desktop
dotnet restore DeskPet.Tests/DeskPet.Tests.csproj --locked-mode
dotnet build DeskPet.Tests/DeskPet.Tests.csproj -c Release -m:1 --no-restore
dotnet restore DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj --locked-mode
dotnet build DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj -c Release -m:1 --no-restore
Pop-Location

# 按相应阶段运行
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --localization-only
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --localization-ui-only
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --agent-jobs-only
# 下项仅 Windows
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --agent-jobs-native-only
python tests/AgentJobRegistrationTests.py

# 总回归，不替代原清单的各原生/UI/独立退出目标
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --report-json work/validation/portable-report.json
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target App -OutputDirectory work/validation/wpf-app
```

完整后台 Framework/UI/退出命令沿用第三份输入第 4 节；语言成品独立运行与安装包验证不能仅用 App 编译代替。

证据分别保存到忽略目录 `work/localization-validation/<sha>/`、`work/agent-jobs-validation/<sha>/`，包含 suite 实际断言数、每项日志、截图、计时与 fixture 清单。每项至少记录 id/platform/suite/case/status/sha/command/duration_ms/budget_ms/log/cleanup_confirmed，未运行标 `not_run`。

原生失败追加阶段、OS/Framework/SDK/架构、异常/Win32 码、Job 活动数、流状态、root_exit_code、游标/配额/尾部和清理结果。进程身份使用自有句柄与创建身份，不按进程名结束。独立退出测试控制器不把宿主退出本身视为通过。

本机、远端 CI、真实浏览器和平台结果分开。没有推送授权则不为 CI 推送；所需平台证据缺失时报告“本地工作已完成、整体验收待补”，不能宣布全部交付通过。

## 6. 工时、回滚和交付定义

语言原计划 S1–S7 合并了原 8 个任务，沿用 **22–37 小时，约 3–5 个有效工作日**；候选统计是旧基线，S1 后按实际范围修订。后台两份输入没有可靠工时估算，且缺设计合同，因此不虚构总工期：S0/S8 补齐设计与基线后，分别估算核心/原生、WPF、Avalonia 和验收。SDK、AppContainer、真实 UI 环境及 CI 排队单列，不混入纯开发工时。

- 每个迁移批次资源与引用同批交付；功能批次保持可编译、测试登记可达。
- 语言阶段无用户数据格式迁移，回退相应代码/词条批次，不删其他模块仍依赖的共享资源。
- 后台回退先拒绝新启动并确认自有任务已清理，再替换应用；保留隔离任务记录，不按历史 PID 清理、不恢复或重跑命令。用户旧配置字段保持向后兼容。
- 最终交付：唯一中文资源、完整双端迁移、审计门禁、共享后台核心与 Windows 执行器、WPF/Avalonia UI/退出、维护/使用文档、L/A/F 验收矩阵。
- 语言阶段通过可单独报告，但不代表后台完成；WPF 后台通过可报告阶段完成，但不代表 Avalonia 或整体完成。所有适用必需项齐备才宣布整体完成，A31 的 P1 结果单列，失败和影响不隐藏。
- 本轮最终产物只有本开发计划。后续实施、提交、推送、合并与发布依据新的明确任务执行。

## 7. 本计划自查

- [x] 已阅读三份输入，保持 JSON 优先，后台原任务 1–10 和测试清单均有统一任务归属。
- [x] 已覆盖 L01–L24、A01–A32、F01–F06，并区分阶段、平台、运行时、逻辑与真实执行证据。
- [x] 已说明缺失设计资料和实施基线漂移，没有把旧统计或计划目标当成当前事实。
- [x] 已统一新增后台文案与 JSON 门禁，保留模型/协议/数据边界及旧功能回归。
- [x] 本轮只新增此计划，未运行任何计划中的构建、应用测试或后台任务。
