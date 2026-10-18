# DeskPet 内置 Agent 向主流编程 Agent 演进实施计划

> **For agentic workers:** 用户明确要求执行本计划后，使用 `superpowers:executing-plans` 按任务逐项实施、验证和记录。读取本文件本身不代表获得修改代码、运行外部任务或发布的授权。不要求启用子代理；执行者必须遵守所在会话的权限和用户指令。

**Goal:** 在保留桌面伙伴、语音、会话和项目体验的基础上，把内置 Agent 从文件助手升级为能阅读项目、修改代码、执行受控验证、使用扩展并可靠恢复任务的编程 Agent。

**Architecture:** 沿用现有 C# 工具循环和共享核心，通过能力解析、统一工具权限、受控进程执行、可恢复任务及真实子任务 worker 逐步补齐执行闭环。桌面 UI 负责展示、输入与确认，核心执行不能依赖 WPF；未接通的能力不得出现在可执行工具列表里。分阶段交付，不整体重写桌宠应用。

**Tech Stack:** Windows WPF / .NET Framework 4.8 构建链，C# 共享核心，.NET 10 共享测试，现有 Avalonia 桌面入口；OpenAI Chat Completions 兼容接口与 Anthropic 协议；MCP、现有本地会话/任务存储。

**Spec:** 本文第 1–6 节是本计划的自包含需求和设计规格，第 7 节开始为实施任务；不依赖原聊天记录。文件创建日期：2026-10-18。

---

## 1. 执行者先读：范围、顺序、授权

- 当前仓库预期在 `E:\workspace\deskpet\agent-enhancement`。`E:\workspace\deskpet` 是外层工作目录，不要误把外层当作 Git 仓库。
- 本计划编写时 HEAD 为 `a2f91dd`，已有大量未提交修改，且另一会话正在修改工作设置、全量核对和配置引导。这只是历史定位信息，执行前必须重新读取当前状态，不能照旧快照覆盖现有文件。
- 本次交付仅是桌面上的计划文档。只有后续用户明确要求“执行计划”时，才开展代码实施。
- 后续执行顺序必须是：核对最新基线 → 补齐已批准的 Agent 能力 → 完成设置与兼容 → 全量核对并修正问题 → **最后重做/更新配置引导** → 本地交付。若主线程已经完成其中某项，验证并复用，不重复重做。
- 当前用户明确要求暂不发布。不得擅自推送、合并、打标签、创建发行版、部署服务、发送外部消息。真实账号写入、付费模型验收和系统权限变更必须分别取得明确授权。
- 不恢复已删除的“项目命令”配置页、命令 ID 配方编辑器或历史命令授权。新增终端能力应使用新的执行策略与确认流程。
- 不把“完整文件访问”解释成拥有终端、管理员、网络、MCP 或任意桌面控制权限。
- 不改用户正在测试的预览安装目录、不重启其应用，除非后续明确要求更新预览。所有测试先使用隔离数据目录。
- 不删除用户数据、旧设置、文件备份或未知修改；不得执行 `git reset --hard`、`git clean -fd` 来制造干净工作区。
- 不为赶进度更换整个框架、迁移 UI 技术栈或引入另一个完整 Agent 产品作为隐藏依赖。

## 2. 对齐目标与非目标

### 2.1 本轮必须形成的闭环

1. 用户提出编程任务，Agent 读取真实项目并形成计划。
2. 工具目录、提示词、UI 与实际权限保持一致。
3. 修改代码后，能够在用户授权的执行环境中运行测试/构建，读取真实退出码与输出，根据失败继续修正。
4. 必要时暂停等待用户确认；用户拒绝或停止后，不继续执行相关操作。
5. 支持加载受信任的项目说明和技能，连接用户配置的 MCP。
6. 可以把只读探索/验证子任务交给真正独立的模型 worker；主 Agent 收到的结果来自 worker，而不是自己填写“完成”。
7. 中断、重启、存储失败后，知道哪些操作已执行、哪些只是计划、哪些需要用户核对，避免盲目重复副作用。
8. 用户能从 UI 看到当前能力、权限、任务状态、验证结果及限制。

### 2.2 本轮不承诺

- 不承诺达到 Claude Code / Codex 的模型推理水平、成功率或速度；本轮对齐的是执行框架和可用能力。
- 不做浏览器自动化、任意桌面点击、无人值守系统运维、自动部署或自动发布。
- 不把对话摘要称为长期记忆，不把普通工作目录称为操作系统沙箱，不把进程 Job Object 称为文件/网络隔离。
- 不同时实现所有平台的安全沙箱。Windows 是第一验收入口；其他平台不具备实现时必须明确禁用，不能静默降级。
- 不默认开启自主写入长期记忆、远程服务、扩展进程、周期任务或未授权外部调用。

## 3. 开始前需要重新核对的当前事实

以下结论来自编写时源码，执行者必须逐条确认；主线程后续可能已修正：

| 项目 | 编写时观察 | 执行要求 |
|---|---|---|
| 原生循环 | `NativeAgentClient.cs` 能调用真实工具并持续处理结果 | 保留，渐进抽取职责，不另造第二套循环 |
| 旧项目命令 | `ConversationSession.OpenAgentWorkspace` 使用 `ConfigureCommands(false,"[]")` | 保持历史授权停用；新终端走独立策略 |
| 工具可用性 | 近期已新增 schema 与调用入口的可用性过滤 | 复用该门禁，禁止未来接入绕过它 |
| GitHub 写入 | 有工具定义；应用缺少真正审批提供者 | 先接通确认，再开放 schema；此前保持不可执行 |
| 子 Agent | 生命周期、claim、调度已有实现，模型执行 worker 缺失 | 补 worker，不把状态记录当作真实执行 |
| MCP | HTTP/stdio 适配器存在，应用用户连接入口未完整接通 | 补配置、连接、权限、状态和退出清理 |
| 模型能力 | `AgentProviderRegistry` 部分按名称/厂商猜测 | 用统一能力配置替换此判断 |
| 上下文 | 已改为已知容量优先、未知时兜底；仍有历史条数/字符截取 | 保留新语义，统一历史构造与压缩策略 |
| 工作设置 | 已重设计为分组单页；项目命令页已移除 | 继续使用该结构；先读最新实现 |
| 跨平台入口 | Avalonia 曾保留旧预算文案与上限 | 检查并同步；不能只改 WPF 而宣称全部入口一致 |
| 全量检查 | `docs/local-comprehensive-review.md` 已记录近期发现和失败夹具 | 读最新报告，未通过不能改写成通过 |

优先阅读：

```text
src/NativeAgentClient.cs
src/ConversationSession.cs
src/ConversationWorkspace.cs
src/ConversationWorkspaceManager.cs
src/ConversationOwnership.cs                 # 若最新基线已有
src/AgentWorkspace.cs
src/AgentToolCatalog.cs
src/AgentTaskState.cs
src/AgentTaskStore.cs
src/AgentContextBudget.cs
src/AgentKernel/AgentPermission.cs
src/AgentKernel/AgentProfile.cs
src/AgentKernel/AgentProvider.cs
src/AgentKernel/AgentToolRuntime.cs
src/AgentKernel/AgentSubagent.cs
src/AgentKernel/AgentSubagentScheduler.cs
src/AgentKernel/McpToolAdapter.cs
src/AgentKernel/SessionModel.cs
src/AgentKernel/SessionStore.cs
src/WindowsCommandRunner.cs
src/AgentCommand.cs
src/Settings.cs
src/ProjectCatalog.cs
src/WorkSettingsPanel.cs
src/ConnectionSettingsPanel.cs
src/PetBusiness.cs
desktop/DeskPet.Core/DeskPet.Core.csproj
desktop/DeskPet.Desktop/WorkSettingsView.cs
desktop/DeskPet.Tests/PortableTests.cs
Build.ps1
.github/workflows/check.yml
tools/RunLocalReview.ps1
docs/local-comprehensive-review.md
docs/dev-0.6.3-review-fixes.md
```

## 4. 统一架构与数据契约

下面是目标契约。可以为适应现有类型调整名称，但若改变，必须先在实施记录中写明映射，不能产生同名异义的两套实现。

### 4.1 能力与权限分开

- **模型能力**：模型能否理解图片、使用工具、输出多工具调用，以及上下文容量。
- **运行时能力**：工具是否注册、依赖是否可用、连接是否建立、子任务 worker 是否真正存在。
- **用户授权**：这次任务能否使用该工具，对哪个资源，是否需确认。
- **执行隔离**：操作系统实际限制了哪些文件、网络及进程能力。
- 工具只有同时通过以上适用条件，才能被执行。schema 过滤是可用性提示，执行入口必须再次校验。

建议核心契约：

```csharp
enum CapabilitySupport { Unknown, Supported, Unsupported }

// 返回不可变快照；不要让运行中的任务读取 UI 草稿。
AgentCapabilitySnapshot AgentCapabilityResolver.Resolve(
    PetSettings settings, ProjectDefinition project);

Task<AgentPermissionDecision> IAgentAuthorizationService.AuthorizeAsync(
    AgentToolInvocation invocation, CancellationToken cancellationToken);

Task<AgentCommandResult> IAgentCommandExecutor.ExecuteAsync(
    AgentCommandRequest request,
    IProgress<AgentProcessEvent> progress,
    CancellationToken cancellationToken);

Task<AgentRunResult> IAgentTaskRunner.RunAsync(
    AgentRunRequest request, CancellationToken cancellationToken);

Task<AgentChildResult> IAgentSubagentRunner.RunAsync(
    AgentChildRequest request, CancellationToken cancellationToken);
```

必要数据：

- `AgentToolInvocation`：`SessionId, ProjectId, TaskId, AgentId, CallId, ToolName, Action, Resource, Arguments, PayloadHash`。
- `AgentCommandRequest`：上述归属 ID、`Command, WorkingDirectory, Shell, TimeoutSeconds`；审批后这些值不可再变。
- `AgentCommandResult`：`ExecutionId, ExitCode, TimedOut, Cancelled, OutputId, StartedUtc, FinishedUtc, IsolationDescription`。
- `AgentChildRequest`：父任务 ID、子任务 ID、输入、只读权限快照、工具白名单、深度、预算。
- `AgentChildResult`：实际状态、结论、工具执行摘要、验证结果、错误与限制；不得由父模型直接标记成功。
- 所有持久化 DTO 遵守现有序列化习惯；新字段必须有旧版本缺省值和迁移测试。
- 核心类型不能引用 `Window`、`Dispatcher`、`MessageBox`；WPF 通过适配器实现审批界面。

### 4.2 执行策略

新增终端策略为 `disabled / confirm_each / sandboxed`：

- 升级旧配置后一律为 `disabled`，旧 `CommandsEnabled` 和命令配方不迁移成新授权。
- `confirm_each`：每次展示准确命令、目录、网络和隔离说明，由用户确认。没有实际 OS 隔离时明确显示“当前账户执行”，不得称“仅工作目录沙箱”。
- `sandboxed`：只有通过实际文件和网络隔离验证的 provider 才能选择；依赖不可用时阻止执行，不自动回落到无隔离模式。
- 项目范围的文件权限与终端策略是两项设置；切换完整文件访问不自动开启终端。
- 初版不做自然语言风险分类器自动放行任意命令，不靠“出现 git/npm 就安全”的字符串匹配规则授权。
- 网络是独立授权维度：允许模型 API 请求，不代表允许执行进程联网。

### 4.3 停止和副作用

- 父任务取消必须传播到模型请求、审批等待、子 worker、命令进程树和 MCP 调用。
- 默认外部命令超时 120 秒，可配置 1–600 秒；长服务和后台常驻进程暂不支持。
- 工具提交到执行前，先持久化稳定执行 ID 与状态；写文件、创建 Issue 等副作用不做无条件自动重试。
- 崩溃后遇到 `running` 的外部副作用，标为“结果待核对”，通过真实状态核对后再决定继续，不能直接重跑。
- 不承诺所有外部操作 exactly-once；API 幂等键、查询核对和人工确认分别处理。

### 4.4 上下文

- 模型上下文容量来源：用户显式覆盖 > 服务返回的可验证元数据 > 有出处且匹配模型的目录元数据 > 工作设置兜底。
- 不再使用厂商名称作为精确容量事实。未知容量必须显示未知/兜底。
- 输入预算必须扣除输出预留、工具 schema 和协议开销；模型窗口总量与可用输入预算在 UI 中分开表达。
- 输出预留默认 `min(8192, max(256, capacity / 8))` Token；另留 `min(2048, max(128, capacity / 32))` 安全空间，再扣实际 schema 估算。若预留和必要当前输入已无法容纳，明确停止并提示缩小请求，不产生负预算。
- 用量达到可用输入预算 90% 时可主动压缩；真正发送前再做容量与请求字节检查。
- 自动压缩必须保留当前用户要求、任务事实、必要系统指令和完整工具调用/结果协议组。摘要失败保留失败状态和可追溯事实，不能补造成功。
- 消除请求构造阶段“先固定丢弃到 10 个问答对，再尝试压缩”的逻辑。历史存储容量上限与模型本次输入预算是两项独立限制。

### 4.5 子 Agent

- 第一版只开放真实只读探索 worker，最大并行 4，最大委派深度 1，不递归启动。
- 子 Agent 继承父授权的子集，不能继承审批 UI 的全局放行状态，不能绕过计划模式或隔离规则。
- 子 Agent 使用独立历史、取消令牌、工具上下文和输出目录；相同文件不得产生无协调的并行写入。
- `spawn_subagent` 返回任务 ID；由真实 worker 调用模型并运行工具。
- 增加只读状态/等待工具，例如 `get_subagent`、`wait_subagents`。等待必须有上限且可取消，不阻塞 UI 线程。
- `complete_subagent` 退出模型可调用 schema，仅保留内部 worker 完成入口。主 Agent 不得自己填写子任务完成结果。
- 未实现 worker 或依赖断开时，schema 不公布，直接调用也明确拒绝。

## 5. 文件拆分地图

现有文件继续承担现有职责；新增文件尽量以 partial 或小服务接入，避免继续扩大 `ConversationSession.cs` 和 `AgentWorkspace.cs`。

| 建议新增文件 | 单一职责 |
|---|---|
| `src/AgentKernel/AgentCapabilityResolver.cs` | 统一模型与运行时能力快照、来源与可用性 |
| `src/AgentKernel/AgentAuthorizationService.cs` | 统一工具授权、审批缓存范围与有效载荷绑定 |
| `src/AgentApprovalController.cs` | WPF 异步确认适配，不承担核心授权规则 |
| `src/AgentKernel/AgentCommandExecution.cs` | 新命令请求/结果契约与输出状态 |
| `src/AgentKernel/AgentExecutionIsolation.cs` | 隔离 provider、实际限制描述、可用性检测 |
| `src/AgentKernel/AgentRunJournal.cs` | 工具执行状态、恢复与副作用核对 |
| `src/AgentKernel/AgentContextPolicy.cs` | 统一窗口预算、预留、历史选择与压缩触发 |
| `src/AgentKernel/AgentInstructions.cs` | 项目说明发现、信任、来源和范围 |
| `src/AgentKernel/AgentSkills.cs` | 技能目录、按需加载、版本及资源路径 |
| `src/AgentKernel/McpConnectionManager.cs` | 服务器连接、工具快照、取消与生命周期 |
| `src/McpSettingsPanel.cs` | MCP 配置、连接状态与权限 UI |
| `src/AgentKernel/AgentTaskRunner.cs` | 将现有循环适配为可复用的非 UI 执行入口 |
| `src/AgentKernel/AgentSubagentRunner.cs` | 真正的子模型/工具循环与结果回传 |
| `src/AgentKernel/AgentTaskMemory.cs` | 后续可选的来源可查、用户可控事实记忆 |
| `src/OnboardingSteps.cs` | 最后阶段的稳定引导步骤定义与完成条件 |

如最新代码已有等价实现，直接复用，不创建并行系统。新共享文件须核对 `desktop/DeskPet.Core/DeskPet.Core.csproj` 的包含规则；非 `AgentKernel` 目录的文件通常需要显式加入。WPF 文件不得进入共享核心项目。

## 6. Global Constraints / Review Focus

### Global Constraints

- 保留现有 WPF 和 .NET Framework 构建兼容；共享核心仍能被 .NET 10 编译。不要直接使用只在现代 .NET 才有的 Process/JSON API。
- 原生 Windows 子进程继续隐藏控制台，除非用户明确要求显示交互窗口。
- 所有授权和执行快照绑定具体会话/项目/任务；运行中切换 UI 不更换任务权限。
- 不自动获取管理员权限；用户拒绝后不换路径重试同一被拒绝行为。
- API Key、令牌、密码不进入命令行、日志、审批正文和测试报告。任意程序输出仍可能含敏感信息，不能承诺万能脱敏。
- 审批、任务恢复、MCP 和模型测试均不能在启动时自动触发真实付费/写入操作。
- 新设置必须遵守全局/项目范围；执行过程与预测消息等全局偏好不能伪装成项目保存项。
- 用户已保存的数据必须保留；存储失败不先删除原文件、不报告保存成功。
- 真实 UI 检查包括文本、开关状态、菜单、滚动、最大化、重开和持久化，不只检查后台属性。

### Review Focus

1. **目录含中文、空格、引号、链接和相邻前缀目录**：命令参数与文件授权指向准确对象，不能越界或错误拼接。
2. **审批等待时停止、切换项目或窗口关闭**：没有悬挂等待，没有重复执行，没有批准错会话。
3. **命令生成子进程并大量输出**：停止/超时关闭整个进程树，输出有界，UI 不冻结。
4. **执行副作用后存储失败或崩溃**：恢复不盲目重放，明确显示待核对状态。
5. **未知模型、断开的 MCP、失败子任务**：不把不可用工具发给模型，不把失败记录当成功结果。

---

## 7. 实施顺序与阶段门禁

```text
任务 0 基线与隔离
  └─任务 1 统一能力
       └─任务 2 审批闭环
            ├─任务 3 新终端与输出
            │    └─任务 4 实际隔离与编程验证
            ├─任务 5 上下文与历史
            ├─任务 6 项目说明与 Skills
            └─任务 7 MCP 接入
                   └─任务 8 可复用任务 runner
                        └─任务 9 真实只读子 Agent
                             └─任务 10 恢复与副作用核对
                                  └─任务 11 可选记忆与验收记录
                                       └─任务 12 设置和跨入口一致性
                                            └─任务 13 全量核对
                                                 └─任务 14 最后更新配置引导
                                                      └─任务 15 本地交付
```

- 优先完成任务 0–5，先得到“读取→修改→授权执行测试→修正→验证”的单 Agent 闭环，再扩展。
- 任务 11 的自动长期记忆不作为编程闭环的阻塞项；先交付手动记忆与真实验收记录，自动沉淀另行授权。
- 任务 4 中真正的 OS 隔离若受平台/依赖阻塞，必须说明阻塞，不能把未隔离模式改名为 sandboxed 过关。是否接受先交付每次确认模式，由用户明确选择。
- 未完成前一门禁，不通过启用假工具或降低测试断言来推进后续阶段。

### 通用任务执行循环

每一任务均按：添加有意义的失败测试 → 运行并观察预期失败 → 最小实现 → 跑所属测试及受影响回归 → 检查 diff → 更新实施记录。

新增测试 Target 时同步注册 `Build.ps1` 的 ValidateSet、必要资源和 CI；红灯应来自未满足行为，不得把无法编译/缺依赖当成正确业务红灯。不要为静态文案或简单可逆改动制造大量实现镜像测试。

---

## 任务 0：建立可恢复基线，不打断当前工作

**Files:** 读取当前 Git 状态、上文源码和现有审计报告；在执行者自己的获准工作目录创建实施记录，不修改用户预览数据。

**Deliverable:** 当前已完成项/未完成项清单、真实测试基线和后续改动隔离方式。

- [ ] 确认仓库绝对路径、分支、HEAD、未提交修改及主线程是否仍在写同一批文件。
- [ ] 若仍有会话修改目标文件，暂停冲突部分，先协调切换；不得自行 stash、覆盖或从旧 HEAD 创建丢失本地成果的工作树。
- [ ] 记录 `git diff --stat` 与需要保留的未跟踪文件；经明确安排后再选择当前 checkout 或包含全部必要本地成果的隔离 checkout。
- [ ] 读最新 `docs/local-comprehensive-review.md`，复核其中未通过项；不要沿用旧的“全部通过”数字。
- [ ] 先跑与本阶段相关的基线测试，记录日志、失败原因和环境限制。需要模型测试时使用本地 fixture，不读取用户真实 Key。
- [ ] 创建逐任务状态记录：未开始/进行中/通过/阻塞，附实际证据路径。

**验收:** 原修改和用户数据完整；实施者能清楚说明本计划哪些事项已在最新源码中完成。

## 任务 1：统一能力来源与工具可用性

**Files:** 修改 `src/AgentKernel/AgentProvider.cs`、`src/AgentToolCatalog.cs`、`src/AgentWorkspace.cs`、`src/Settings.cs`、`src/TextProviderEditor.cs`、`src/ModelConfigurationUi.cs`；新增 `src/AgentKernel/AgentCapabilityResolver.cs`、`tests/AgentCapabilityTests.cs`。

**Interfaces:** 产出 `AgentCapabilitySnapshot` 和 `AgentCapabilityResolver.Resolve(...)`；所有后续任务都从这里读取能力，不再按名称重新推断。

- [ ] 红灯：兼容服务中的未知名称模型，显式配置支持工具后应拿到工具；显式关闭工具后不得拿到；容量覆盖优先级正确；更换服务商不串用元数据。
- [ ] 增加工具、图片等三态能力配置及来源说明。元数据没有提供时保持 Unknown，不能直接判定 Unsupported。
- [ ] 已有旧模型按旧行为兼容迁移，但把名称推断标为 legacy/未验证；用户覆盖和服务端能力优先。不要把协议兼容性等同于工具支持。
- [ ] 统一 schema 过滤、实际 Execute 校验、设置页和上下文预览所使用的快照。
- [ ] 从实际可用工具生成提示词中的能力段；禁用命令时不得再要求调用 `list_commands` 验证。
- [ ] 添加测试目标并运行 `AgentCapabilityTests`、`ProviderTests`、`ProviderUiSmoke`、`AgentProviderKernelTests`、`AgentProtocolTests`。

**验收:** 改模型配置能实际影响请求 schema；不存在“UI 开启但入口无依赖”“schema 移除但直接调用仍可执行”的分叉。

## 任务 2：接通真实异步审批

**Files:** 修改 `src/AgentKernel/AgentPermission.cs`、`src/AgentKernel/AgentToolRuntime.cs`、`src/ConversationSession.cs`、`src/AgentWorkspace.cs`、`src/AgentKernel/AgentAudit.cs`；新增 `src/AgentKernel/AgentAuthorizationService.cs`、`src/AgentApprovalController.cs`、`tests/AgentApprovalTests.cs`、`tests/AgentApprovalUiSmoke.cs`。

**Interfaces:** 消费任务 1 的快照，产出 `IAgentAuthorizationService.AuthorizeAsync(invocation, ct)` 和 WPF `IAgentApprovalProvider` 适配。

- [ ] 红灯：Ask 显示正确资源；拒绝后无副作用；停止后取消等待；切项目不批准另一个任务；同一 CallId 重复投递不执行两次。
- [ ] 审批记录绑定精确 payload hash、CallId、任务/项目和权限版本；审批后参数变更使授权失效。
- [ ] 第一版提供“仅本次允许/拒绝”。如增加“本任务允许”，必须只覆盖相同工具和精确资源范围，不覆盖任意命令、任意仓库或未来任务。
- [ ] 审批卡显示操作、目标、范围及可能写入的摘要；完整敏感正文不写日志。界面可展开查看必要参数。
- [ ] UI 等待使用异步 TCS 和取消回调，不在工作线程调用同步 MessageBox，不在锁内等待 Dispatcher。
- [ ] 审批提供者不存在时维持 fail-closed；界面显示“当前入口不支持确认”，不要伪装成用户拒绝。
- [ ] 接通 GitHub 写入审批后才开放对应 schema；用 mock HTTP 验证允许、拒绝、超时和取消，不向真实仓库写入。
- [ ] 运行审批测试、`GitHubToolsTests`、`AgentProfileTests`、`AgentKernelTests` 和会话取消回归。

**验收:** 一次真实 UI 点击可允许隔离 fixture 副作用；拒绝、关闭、取消都不会执行；审计能追溯决定但不泄漏凭证。

## 任务 3：新增受控终端与可读取输出

**Files:** 修改 `src/WindowsCommandRunner.cs`、`src/AgentWorkspace.cs`、`src/AgentToolCatalog.cs`、`src/AgentTaskState.cs`、`src/AgentTaskPanel.cs`、`src/Settings.cs`、`src/ProjectCatalog.cs`；新增 `src/AgentKernel/AgentCommandExecution.cs`、`tests/AgentTerminalTests.cs`。

**Interfaces:** 实现 `IAgentCommandExecutor.ExecuteAsync(request, progress, ct)`；工具名采用 `exec_command`，不是旧 `run_command(command_id)`。

- [ ] 红灯：策略 Disabled 时 schema 无终端且直接调用拒绝；confirm_each 未批准不能启动进程；批准后返回真实退出码、输出 ID。
- [ ] `exec_command` 参数明确为命令文本、工作目录和超时，不允许模型修改执行归属 ID、授权策略或隔离标记。
- [ ] 复用/抽取现有 suspended spawn + Job Object 的进程树清理；不要退化成只能杀父进程的 `Process.Kill()`。
- [ ] Windows 默认使用明确解析的 PowerShell 可执行路径，参数通过可靠 quoting 或 EncodedCommand 传递；不得把 JSON.stringify 当作 shell quoting。
- [ ] 支持 Unicode、中文路径、空格、引号和多行脚本。禁止把用户 Key 注入命令行；环境变量仅注入必要白名单，不加载扩展提供的任意 profile。
- [ ] stdout/stderr 分流标识并有界保存：保留现有 256 KiB 级内存上限；单次模型返回不超过 12000 字符；完整有界输出通过 `read_output(output_id, offset, limit)` 分页。更大输出写临时日志并设置明确磁盘上限。
- [ ] 进程启动前记录 executionId；超时、取消、输出截断分别返回明确状态，不把非零退出码当成功。
- [ ] 升级后新终端默认 Disabled；旧配方保留为兼容数据或显式归档，不成为可执行白名单。
- [ ] 用隔离脚本实测：退出码 7；Unicode 输出；超过上限输出；父进程启动孙进程；停止后所有该任务后代退出，其他进程不受影响。
- [ ] 运行 `AgentTerminalTests`、现有 `AgentCommandTests`、`AgentProtocolTests`、`AgentReliabilityTests`。

**验收:** Agent 能在获得确认后运行隔离项目测试并看到失败；停止和超时无残留任务进程；没有恢复旧命令 UI。

## 任务 4：实际隔离与编程验证闭环

**Files:** 新增 `src/AgentKernel/AgentExecutionIsolation.cs`、`tests/AgentIsolationTests.cs`、`tests/AgentCodingWorkflowTests.cs`；修改执行器、权限服务、`src/NativeAgentClient.cs`、`src/AgentTaskState.cs` 与工作设置。

**Interfaces:** `IExecutionIsolationProvider` 提供实际文件/网络限制、可用性和 Prepare/Start 生命周期。`IsolationDescription` 必须来自执行器，不接收模型自述。

- [ ] 先区分 `current-account-confirmed` 与真正 `sandboxed`；没有 provider 的平台不提供 sandboxed 开关。
- [ ] 调研并选择一个 Windows 可维护方案：基于系统限制令牌/ACL 与网络隔离的实现，或明确的隔离运行环境。先验证 PoC，再接产品；不得只改工作目录就宣称隔离完成。
- [ ] 红灯：沙箱脚本无法写隔离 fixture 外部目录，无法越过 symlink/junction，网络被关闭时无法访问本地模拟 HTTP；workspace 目录内正常构建可用。
- [ ] 测试证明 Job Object 只负责进程生命周期，文件与网络约束另有真实机制；provider 初始化失败必须拒绝，不能静默降级。
- [ ] 建立真实小项目 fixture：代码有 bug、两个测试，其中一个失败；Agent 读代码、修改、执行测试，看到真实通过后才完成。
- [ ] 测试/构建结果记录命令、执行 ID、目录、退出码和时间；用户停止或环境缺依赖时显示“未验证/受阻”，不能标通过。
- [ ] 增加 Git 只读状态/diff 入口或通过已授权终端读取；提交、推送、合并、发布不得由“测试通过”自动触发。
- [ ] 保存已有工作目录和未提交文件；实际修改仅在临时 fixture 中进行。

**验收:** 至少一个真实编译/测试链路通过，且一次故意失败和一次拒绝均正确报告。未实现的隔离项目保留未完成状态，由用户决定是否接受有限交付。

## 任务 5：统一历史、模型窗口与自动压缩

**Files:** 修改 `src/PetBusiness.cs`、`src/AgentContextBudget.cs`、`src/ConversationContextUsage.cs`、`src/ComposerContextUsage.cs`、`src/NativeAgentClient.cs`、`src/Settings.cs`；新增 `src/AgentKernel/AgentContextPolicy.cs`、`tests/AgentContextPolicyTests.cs`；扩展现有上下文测试。

**Interfaces:** 统一 `AgentContextPolicy` 返回容量来源、输入预算、输出预留、工具开销、压缩阈值；普通对话和 Agent 使用同一预算契约。

- [ ] 红灯：131072 已知容量不被默认 32768 截断；未知容量采用兜底；人工覆盖优先；模型切换不串容量。
- [ ] 红灯：超过 10 轮但仍能容纳的历史不被固定丢弃；超出预算会先产生可追溯摘要；当前输入和附件不被静默删除。
- [ ] 历史选择按预算和完整会话/工具协议单元进行，替换固定条数截断；存储保留上限作为独立策略，不无界加载整个历史。
- [ ] 按第 4.4 节执行预留计算；provider 返回真实 usage 时用于展示与校准，发送前估算仍明确标注，不混同计费数据。
- [ ] 为工具结果、thinking/signature、附件以及 Anthropic/OpenAI 两种协议分别测试压缩后的有效性。
- [ ] 摘要服务失败、用户取消、超长当前输入、必要工具组超过窗口时，给出明确失败，不无穷重试压缩。
- [ ] 运行 `AgentContextPolicyTests`、`ContextUsageTests`、`AgentContextTests`、`AgentContextRecoveryTests`、`AgentProtocolTests`、`HistoryTests`、`SteeringSmoke`。

**验收:** 大窗口确实能携带更多有效历史；预览的预算与实际请求一致；不会发送孤立工具结果或把摘要说成精确原始历史。

## 任务 6：项目说明与 Skills 按需加载

**Files:** 新增 `src/AgentKernel/AgentInstructions.cs`、`src/AgentKernel/AgentSkills.cs`、`tests/AgentInstructionTests.cs`、`tests/AgentSkillsTests.cs`；修改工具目录、授权服务和上下文构造。

**Interfaces:** 说明文件返回来源路径、内容 hash 和适用目录；技能目录返回名称/描述/来源，`load_skill(name)` 才读取完整正文。

- [ ] 红灯：当前项目和祖先项目的 AGENTS.md 依作用范围生效；越界路径、循环引用、未信任项目和超大说明被明确处理。
- [ ] 第一版支持 `AGENTS.md` 和用户明确配置的全局说明；`CLAUDE.md` 作为兼容选项，不能重复叠加同一份内容造成冲突。
- [ ] 技能发现支持明确的用户目录与项目 `.agents/skills/<name>/SKILL.md`；项目内可执行扩展和脚本不因发现文件而自动运行。
- [ ] 解析 name/description，拒绝无效名称、重名歧义和路径逃逸。每个说明/技能正文上限 64 KiB；超限显示原因，不能截断关键权限段后当完整内容加载。
- [ ] 目录只注入简短元数据，正文按需加载；引用资源路径基于技能目录解析，不能越出获准范围。
- [ ] 项目文件与技能内容不能覆盖系统/开发者/用户指令，也不能扩大工具权限；加载结果保留来源标记。
- [ ] slash 菜单中的技能项必须来自实际目录；没有加载机制时不显示假技能。
- [ ] 用本地技能完成一项固定格式的代码审查 fixture；同时验证恶意技能要求“忽略权限”时不能调用被禁用终端。

**验收:** 技能从发现、选择、加载到结果可见；没有隐式脚本执行或未授权联网安装。

## 任务 7：MCP 配置、连接与工具生命周期

**Files:** 修改 `src/AgentKernel/McpToolAdapter.cs`、`src/AgentWorkspace.cs`、`src/AgentToolCatalog.cs`、`src/Settings.cs`、`src/SettingsWindow.cs`；新增 `src/AgentKernel/McpConnectionManager.cs`、`src/McpSettingsPanel.cs`、`tests/McpConnectionTests.cs`、`tests/McpUiSmoke.cs`。

**Interfaces:** `ConnectAsync(serverId, ct)`、`DisconnectAsync(serverId, ct)`、`GetTools(serverId)`、`CallAsync(serverId, toolId, args, invocation, ct)`；连接状态不与某个 WPF 控件生命周期绑定。

- [ ] 红灯：用户未连接的服务器不启动进程、不发网络请求、不产生 schema；连接成功才公布真实工具。
- [ ] 配置服务器 ID、传输类型、地址或可执行文件及参数、独立凭证、启用状态和工具授权。秘密继续使用现有加密存储，不保存明文进程参数。
- [ ] stdio/HTTP 初始化、tools/list、tools/call、错误响应、取消和断线均走真实本地 fixture；只宣布已经实测的协议能力，不把简化 JSON POST 宣称完整 Streamable HTTP/SSE 支持。
- [ ] 工具稳定 ID 必须包含 serverId，避免两个服务器同名工具冲突；保留多段文本、图片和结构化结果，不只返回第一段文本。
- [ ] MCP 的 readOnly 注解仅供提示，不构成可信授权。未知工具默认要求确认，计划模式仅允许经过策略明确认可的只读工具。
- [ ] 连接动作由用户触发；启动探测不自动拉起 Photoshop、浏览器或其他外部应用。重复进入设置不重复生成服务器进程。
- [ ] 断开/退出/服务器崩溃时清理该连接拥有的进程和工具快照，不杀其他程序；重新连接生成新连接代次，旧审批失效。
- [ ] 运行 MCP 测试、`AgentKernelIntegrationTests`、权限测试、`SettingsNavigationSmoke`；不依赖真实外部 MCP 服务过关。

**验收:** 用户能配置本地假 MCP、连接、调用、拒绝写操作、断开；UI 状态、schema 和实际执行一致。

## 任务 8：抽出非 UI 任务 runner

**Files:** 新增 `src/AgentKernel/AgentTaskRunner.cs`、`tests/AgentTaskRunnerTests.cs`；修改 `src/NativeAgentClient.cs`、`src/ConversationSession.cs`、任务状态及 API 接口的必要部分。

**Interfaces:** `IAgentTaskRunner.RunAsync(request, ct)` 接收模型 API、工具上下文、授权、预算、持久化与事件出口；不直接创建窗口或修改全局当前会话。

- [ ] 先用现有单 Agent fixture 红灯定义隔离运行：独立任务读自己的历史、接收工具结果、返回真正终态。
- [ ] 以适配器包住现有原生循环，再逐步抽取共享逻辑；保持普通对话、流式输出、推理块、工具过程和引导边界行为。
- [ ] 任务状态统一为 queued / running / waiting_approval / completed / failed / cancelled / needs_reconciliation，状态迁移由运行时控制。
- [ ] 每次运行使用不可变 settings/capability/permission 快照；取消旧任务后开始新任务，不复用失效事件出口。
- [ ] 现有 UI 接受 runner 事件并按会话/任务 ID 路由，不靠全局“当前聊天”决定归属。
- [ ] 运行 runner 测试、`ConversationOwnershipTests`、`ConversationWorkspaceTests`、`ConversationWorkspaceManagerTests`、`AgentTaskBindingTests`、`ConversationTests`、`SteeringSmoke`。

**验收:** 同一核心 runner 可在无窗口测试中跑完任务；UI 行为不退化，跨会话事件不串线。

## 任务 9：真实只读子 Agent

**Files:** 新增 `src/AgentKernel/AgentSubagentRunner.cs`、`tests/AgentSubagentExecutionTests.cs`；修改现有子任务管理/调度、工具目录、权限与任务面板。

**Interfaces:** 消费任务 8 runner，建立 `spawn → worker model/tool loop → result → parent observation`；只读 `get_subagent / wait_subagents` 返回实际 worker 状态。

- [ ] 红灯：两个子任务使用不同假模型实例和不同历史，各自调用真实只读 fixture 工具；父任务收到实际结果。
- [ ] 实现并行 4、深度 1 和 FIFO 排队。等待/停止/失败后释放 claim，提升同一父任务的下一项，保留子任务 ID。
- [ ] 子任务权限严格为父权限子集；explore 禁止文件写入、终端和外部写入，不开放递归 spawn。
- [ ] 从模型 schema 移除 `complete_subagent`，由 worker 内部记录结果；父模型伪造完成调用应被拒绝。
- [ ] 子任务单独计预算，并计入父任务总体用量统计；超预算、取消和异常都返回明确状态。
- [ ] 父任务取消传播到全部运行与等待子任务；单个子任务失败不伪装成其他子任务失败或父任务成功。
- [ ] 任务面板能查看子任务输入、状态、工具摘要、结果来源；不把“正在调度”显示成“执行完成”。
- [ ] 运行新测试和 `AgentSubagentToolTests`、`AgentKernelIntegrationTests`、`AgentTaskBindingTests`；旧仅验证状态的测试保留，但不再作为唯一验收。

**验收:** 能证明子任务真正发起了独立模型请求，并且独立读取到不同内容；直接改状态不能冒充验收。

## 任务 10：执行日志、崩溃恢复与副作用核对

**Files:** 新增 `src/AgentKernel/AgentRunJournal.cs`、`tests/AgentRunRecoveryTests.cs`；修改 `src/AgentKernel/SessionModel.cs`、`src/AgentKernel/SessionStore.cs`、任务存储、工具运行时、执行器与恢复 UI。

**Interfaces:** executionId / CallId 驱动的持久状态：prepared / approved / started / completed / failed / cancelled / needs_reconciliation。

- [ ] 红灯：在副作用前、刚完成副作用后、写完成记录失败时分别注入故障，重启后都不能重复盲目执行。
- [ ] 先保存执行意图，再执行；完成后持久化可验证结果。存储异常保留原文件和明确警告，释放 busy，不无限占用任务。
- [ ] 对文件写入保存前后 hash；对终端保留执行状态与输出路径；对远程写入记录外部 ID 或可查询证据，不能仅依靠模型文字。
- [ ] 恢复时通过真实状态判断已完成；无法确认的外部副作用进入待核对状态，提供继续/取消/人工核对入口。
- [ ] 恢复前重新验证项目归属、文件变化和授权。上一次“仅本次允许”不得跨重启自动复用。
- [ ] 对 schema 版本、损坏文件、读写锁、目录权限和旧明文兼容进行测试，不覆盖不可读取的旧状态。
- [ ] 运行恢复新测试、`HistoryTests`、`AgentTaskTests`、`AgentSessionModelTests`、`AgentContextRecoveryTests`、`QueueTests`。

**验收:** 能在 fixture 中复现并证明崩溃恢复不会重复追加文件或重复发 mock API；不能核对时不会伪装成成功。

## 任务 11：可控知识与真实验收记录

**Files:** 按需要新增 `src/AgentKernel/AgentTaskMemory.cs`、`tests/AgentTaskMemoryTests.cs`；修改任务结果与展示，不引入自动画像系统。

**Deliverable:** 每次任务有真实变更/验证/限制摘要；可选的手动保存事实入口。

- [ ] 任务结束结果分开记录：修改了什么、实际运行了什么、退出码/断言、尚未验证什么，不从最终回答反推成功。
- [ ] 可手动保存项目事实，包含来源任务/文件、时间、项目范围和用户可编辑状态；默认不自动跨项目共享。
- [ ] 没有来源、被撤销、包含密钥或仅为模型猜测的内容不得自动进入长期记忆。
- [ ] 用户能够查看、编辑和删除；删除后后续任务不再注入该条目，旧生成缓存必须失效。
- [ ] 自动经验沉淀和自动技能创建作为后续可选能力，不因对标 Hermes 就默认开启。

**验收:** 一条人工确认事实可被下次同项目任务引用，并可删除；任务验证摘要与真实执行记录吻合。

## 任务 12：设置页与各入口一致性

**Files:** 修改 `src/WorkSettingsPanel.cs`、`src/SettingsWindow.cs`、`src/AgentTaskPanel.cs`、`src/ChatWindow.cs`、`src/QuickChatWindow.cs`、`src/ConversationComposer.cs`；同步必要的 Avalonia `WorkSettingsView.cs`、共享设置；扩展 UI 测试。

- [ ] 保持分组单页，明确“全局默认/当前项目”；不得恢复旧项目命令标签页。
- [ ] 新增终端执行策略与独立网络/隔离状态，默认不启用；权限说明从真实快照生成。
- [ ] 模型容量在模型页编辑；工作页保留自动管理和未知模型兜底，不再重复出现含混预算上限。
- [ ] 展示模型支持、工具已连接、用户授权、隔离方式四类不同状态；不可用项给出明确原因。
- [ ] 快捷聊天保留其纯对话语义，不因新工具支持而偷偷获得工作权限；完整聊天使用项目实际权限。
- [ ] 项目移动、切换模型、后台完成、等待审批、停止队列时更新正确会话，不丢草稿与附件。
- [ ] Avalonia 无对应能力时显示不可用，不能保留旧文案却调用新逻辑，不能宣称 Windows 功能已经跨平台实现。
- [ ] 检查默认/小窗口/最大化、长中文路径、模型弹窗、键盘聚焦、屏幕阅读名称；保存重开后值和作用域正确。

**验收:** 用户通过 UI 能正确理解并改变实际执行权限；已保存/未保存/已验证/未验证分开显示。

## 任务 13：配置引导重做前的全量核对

**Files:** 更新实际审计报告与工具清单；维护 `Build.ps1`、CI、共享测试入口，不修改报告掩盖失败。

- [ ] 核对每个公布工具的 schema、执行分支、权限、依赖、状态、取消和存储；对照实际请求确认模型看到什么。
- [ ] 完成相同小项目的端到端验收：定位 bug → 修改 → 执行失败测试 → 修正 → 测试通过 → 返回真实证据。
- [ ] 独立验收：拒绝命令、审批中停止、父任务停止子任务、MCP 断线、存储锁定、重启恢复、跨项目权限隔离。
- [ ] 扫描残留“项目命令”“已支持 MCP/子 Agent”等旧提示词/说明，消除能力宣传与运行时不一致。
- [ ] 全量运行适用测试；原生鼠标、焦点和交互 smoke 串行运行，不与其他 UI 测试争用桌面。
- [ ] 对历史失败夹具逐项修正或明确标明未覆盖原因，不只减少测试数量；fixture 更新不能取消产品行为断言。
- [ ] 审查可进一步优化项，分为本轮正确性阻塞、后续性能/体验改进、明确不做；先修阻塞，再更新引导。
- [ ] 此阶段完成前，不开始任务 14。报告写明测试数量、失败/跳过、真实设备与付费模型测试情况。

**验收:** 核心场景无已知阻塞错误；未完成项可追溯；不存在把测试通过等同真实账号/所有环境可用的结论。

## 任务 14：最后完成详细配置引导

**Files:** 修改 `src/OnboardingWindow.cs`、`src/OnboardingUpgrade.cs`、`src/GuideFocusScope.cs`、`src/ConnectionSettingsPanel.cs`；按需要新增 `src/OnboardingSteps.cs`；更新 `tests/OnboardingSmoke.cs`、`tests/OnboardingModelSmoke.cs`、`tests/OnboardingUpgradeTests.cs` 及共享对应测试。

**Interfaces:** 用稳定步骤 ID 和章节/子步骤描述代替只依赖数字索引的大段 if/else；已有用户进度有迁移表，不把旧第 10 步误映射到新第 10 个小步骤。

- [ ] 每个小步骤只要求一个主要动作，包含“为何需要、在哪里、如何做、预期结果、如何判定完成、失败怎么办”。高亮具体控件，并允许相关弹窗操作。
- [ ] 章节包括伙伴、人格、行为、实际互动、文字模型、ASR、朗读、Live、聊天与附件、项目与权限、新编程能力、上下文、网络与恢复。
- [ ] 已配置项识别为已有配置，用户可以复核或重新测试；不能清空其 Key 或擅自关闭已选能力。
- [ ] 状态区分未配置/草稿/已保存/已验证/跳过/失败；“走完引导”不能表示所有服务都已验证可用。
- [ ] 模型章节按实际逐个 + 添加流程，包含协议、地址、独立 Key、获取列表、添加弹窗、模型能力、上下文来源、手动覆盖、保存和可选真实测试。
- [ ] ASR 包含服务和模型、独立 Key、保存、麦克风权限说明、用户主动测试、草稿识别与取消；不得启动即录音。
- [ ] **云端 TTS 至少细化为下面 12 个可验证小步骤：**
  1. 解释“回复自动朗读”“云端 TTS”“音量”是三个不同设置；关闭云端时的系统语音路径。
  2. 选择系统朗读或云端朗读分支；不用云端可跳过服务字段。
  3. 高亮并操作“使用云端 TTS”，确认开关实际开启。
  4. 新增或选择 TTS 服务商，解释它不自动复用文字模型 Key。
  5. 核对 TTS 协议和服务地址，说明应保留服务要求的路径前缀。
  6. 填写独立 Key 和按需代理；失败提示不显示凭证或完整服务响应。
  7. 核对/选择真正的语音合成模型，不把聊天模型当 TTS 模型。
  8. 选择或手填音色 ID，说明音色随伙伴和服务模型保存。
  9. 设置音量，零音量时说明试听将无声，不暗示连接必然失败。
  10. 选择是否自动朗读回复；此开关与试听本身分别解释。
  11. 保存后，由用户主动点击试听；允许停止，处理加载、成功与失败；请求完成不等于用户确认听到声音。
  12. 用户确认听到、可返回调整；离开再进入/重开后确认云端开关、音色和音量保持。
- [ ] Live 单独解释音频链路、独立 Key、音色、默认语音模式和收音时机；用户主动开始才连接，挂断后停止。不能将配置保存等同通话验证。
- [ ] 新终端按 Disabled / 每次确认 / 已验证沙箱分支讲解；先展示审批演示和安全 fixture，不在引导里静默运行真实项目命令。
- [ ] MCP 和子 Agent 只在真实可用时出现；未配置可跳过，不能高亮到禁用页面把用户锁住。
- [ ] 允许上一步、跳过当前项、跳过整章、稍后继续、直接复习某章；明确跳过不会假装保存无效字段。
- [ ] 试听/连接/录音/测试必须显式用户操作；相关步骤可启用必要控件，不再将所有测试按钮一概禁用。
- [ ] 验证首次配置、已有配置、无账号、获取列表失败、手动添加、试听失败、网络离线、退出恢复、小窗口/最大化、辅助功能和重开持久化。

**验收:** 从零使用隔离设置走完文字模型、系统朗读、云端 TTS、可选 ASR/Live 的完整路径；每次“下一步”的放行理由可解释，没有靠人工改内部字段跳过的隐藏必经操作。

## 任务 15：本地交付和最终报告

- [ ] 在获准目录构建本地预览，保存版本、源码基线、构建时间、exe SHA256、未发布标记和测试报告路径。
- [ ] 只有用户要求更新运行预览时，先识别准确 exe 路径和进程，备份配置/历史，正常关闭并保留数据后替换；不能结束同名但不同路径的程序。
- [ ] 用户要求打开/最大化时，确认真正的完整聊天窗口和当前显示器工作区边界，不只设置后台属性。
- [ ] 最终报告说明：实际新增能力、默认开关、尚未完成能力、测试与真实验收限制、数据迁移及回退方式。
- [ ] 保持“未发布”。不得把生成本地运行包写成已发布，不擅自上传日志、用户数据或包含真实配置的包。

---

## 8. 验证命令与接线要求

下列命令均在实际仓库根目录执行。新增 Target 在相应任务开始时注册；未注册前不能将“未知 Target”视为功能测试失败。

```powershell
Set-Location -LiteralPath 'E:\workspace\deskpet\agent-enhancement'

# 现有基础回归；按阶段选择受影响项，不必每次重复全部执行。
.\Build.ps1 -Target AgentProtocolTests
.\Build.ps1 -Target AgentSessionTests
.\Build.ps1 -Target AgentTaskBindingTests
.\Build.ps1 -Target AgentContextTests
.\Build.ps1 -Target ContextUsageTests
.\Build.ps1 -Target QueueTests
.\Build.ps1 -Target SteeringSmoke
.\Build.ps1 -Target WorkSettingsTests
.\Build.ps1 -Target AgentWorkbenchSmoke

# 新增 Target 示例；必须与任务中新建测试文件同名并加入 ValidateSet。
.\Build.ps1 -Target AgentCapabilityTests
.\Build.ps1 -Target AgentApprovalTests
.\Build.ps1 -Target AgentTerminalTests
.\Build.ps1 -Target AgentIsolationTests
.\Build.ps1 -Target AgentCodingWorkflowTests
.\Build.ps1 -Target AgentSubagentExecutionTests
.\Build.ps1 -Target AgentRunRecoveryTests

# 共享项目；依赖已恢复时用 --no-restore，不能把缺依赖当成产品失败。
dotnet run --no-restore --project desktop/DeskPet.Tests/DeskPet.Tests.csproj -- --report-json work/agent-evolution-portable.json

# 获得本地构建授权后，在隔离目录构建，不覆盖正在运行的用户预览。
.\Build.ps1 -Target App -OutputDirectory 'E:\workspace\deskpet\outputs\agent-evolution-preview'
```

- `Build.ps1` 递归包含 `src`，但共享 `.csproj` 不是对所有源文件自动包含；每次新增文件都要核对两条构建链。
- 同步 `.github/workflows/check.yml`，但不通过推送来替代本地验收；用户未授权时不触发远程发布工作流。
- 共享测试的入口 `PortableTests.cs` 与 `.csproj` 需要显式纳入新增纯核心测试；不能仅添加文件就宣称共享回归已覆盖。
- `tools/RunLocalReview.ps1` 执行前先读当前脚本。若它默认排除旧引导测试，任务 14 后应补专门引导批次，不能靠旧排除规则漏测。
- UI/原生输入测试按串行、可见窗口、正确焦点执行；不以无限重试掩盖真实失败。
- 网络隔离环境中的本机 HTTP fixture 若无法监听，记录环境阻塞并按会话权限处理；不得切到真实外网 API 凑通过。
- 每阶段一次完整必要验证即可；修改、失败或新风险才扩大/重跑，不反复运行无关套件消耗资源。

## 9. 总体验收矩阵

| 场景 | 期望结果 | 证据 |
|---|---|---|
| 只读文件任务 | 不需要终端也能完成；写操作被禁止 | 工具日志、fixture 文件 hash |
| 编程闭环 | 真实修改并跑通测试，失败可继续修正 | 真实命令、退出码、前后测试结果 |
| 命令拒绝 | 没有启动子进程或改变文件 | 进程/文件探针、审批记录 |
| 命令停止/超时 | 该任务整个进程树退出 | 隔离子孙进程探针 |
| OS 沙箱 | 目录外写入/未授权网络实际被阻止 | 反向探针，不只是权限文案 |
| 已知/未知模型 | 能力和上下文来源正确，不靠名称猜测 | 目录响应、显式覆盖、实际请求 |
| 长历史压缩 | 保留要求与完整协议组，失败不捏造摘要 | 请求快照、摘要和取消测试 |
| MCP | 真实连接后调用，断开后立即不可用 | 本地服务器日志、schema 快照 |
| 子 Agent | 独立模型和历史实际运行，父收到真实结果 | 多 worker 请求记录、取消传播 |
| 崩溃恢复 | 已发生副作用不被盲目重复 | 故障注入与外部计数器 |
| 项目/会话隔离 | 不串权限、审批、草稿、输出和历史 | 两项目/两会话 fixture |
| 设置作用域 | 项目设置与全局偏好保存到正确位置 | 重开后的 UI 与持久化值 |
| 配置引导 | 小步骤可完成/跳过/恢复，TTS 可主动试听 | 实际交互截图、状态和重开验证 |
| 本地交付 | 构建可运行且保留用户数据，未发布 | hash、备份、构建与测试报告 |

## 10. 实施报告必须回答的问题

1. 当前版本具体公布给模型哪些工具？哪些只是底层接口？
2. 哪些行为默认允许、默认关闭、每次确认？隔离究竟由什么机制执行？
3. 真实跑了哪些编译/测试命令？哪些失败或没跑？
4. 子 Agent 有没有真正独立调用模型？MCP 有没有真正连上本地 fixture？
5. 是否存在界面说支持、实际入口拒绝，或界面禁用、直接调用仍可执行的情况？
6. 用户拒绝、停止、切换项目、存储失败、程序崩溃后会发生什么？
7. 旧数据如何兼容？旧授权是否仍被停用？如何回退？
8. 引导是否最后更新，并与最终真实界面和能力一致？
9. 哪些属于主流工具架构对齐，哪些只是外观相似？没有同题实测就不要写“达到/超越某产品”。

## 11. 参考资料（执行时重新核对最新版）

本计划借鉴的是明确的能力和边界，不要求逐字复制第三方实现或直接移植其许可不兼容的代码。

- [Claude Code 工作流与扩展](https://code.claude.com/docs/en/overview)
- [Claude Code 子 Agent](https://code.claude.com/docs/en/sub-agents)
- [Claude Code 沙箱边界](https://code.claude.com/docs/en/sandboxing)
- [OpenCode 工具](https://opencode.ai/docs/tools/)
- [OpenCode 权限](https://opencode.ai/docs/permissions/)
- [OpenCode Skills](https://opencode.ai/docs/skills/)
- [Codex CLI](https://learn.chatgpt.com/docs/codex/cli)
- [Codex 沙箱](https://learn.chatgpt.com/docs/sandboxing)
- [Codex 子 Agent](https://learn.chatgpt.com/docs/agent-configuration/subagents)
- [Pi Coding Agent](https://github.com/earendil-works/pi/tree/main/packages/coding-agent)
- [Hermes 工具集](https://hermes-agent.nousresearch.com/docs/user-guide/features/tools)
- [Hermes 委派](https://hermes-agent.nousresearch.com/docs/user-guide/features/delegation)
- [Hermes 安全](https://hermes-agent.nousresearch.com/docs/user-guide/security)
---

## 给执行 Codex 的结束提醒

当用户要求执行时，先完成任务 0，报告最新基线与实际尚缺项，再从最早未完成任务继续。不要因为文件中列了所有阶段就一次铺开修改全部系统；每阶段应有真实可运行结果和失败验证。上下文不足时更新实施记录，续接原任务，不把未完成标成完成。全量核对在配置引导之前，配置引导最后完成，交付保持本地未发布。
