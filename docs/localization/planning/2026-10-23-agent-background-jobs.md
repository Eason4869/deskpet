# Agent Background Jobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. 当前只编写计划，禁止开始实现。

**Goal:** 先完整交付 WPF Agent 后台任务，完成阶段验收后立即接入 Windows Avalonia，统一任务核心、权限、日志和进程回收。

**Architecture:** 应用持有一个 AgentJobManager；短命 AgentWorkspace 只注入可信归属。共享状态/存储与 Windows 可持有 Job Object 执行器分离；两端只实现各自宿主装配和 UI。Avalonia 复用现有持久项目/会话模型补最小绑定，不迁移整个 WPF 侧栏。

**Tech Stack:** C#（共享源码兼容现有 .NET Framework 4.8 编译器）、WPF、.NET 10、Avalonia、Windows Job Object/AppContainer、现有自运行测试程序。无 Docker、无新外部守护进程。

**Spec:** [已认可的设计与 A01–A32 验收](../specs/2026-10-23-agent-background-jobs-design.md)。详细测试执行清单见 [测试清单](2026-10-23-agent-background-jobs-test-checklist.md)。

## Global Constraints

- 本轮仅修改规划文档，不改代码/测试/配置，不执行构建、任务或收费模型请求，不提交或推送。
- 实施顺序：任务 1–7 完成 WPF 阶段 A；随后立即任务 8–10 接入 Avalonia 阶段 B，不能将阶段 A 完成写成整体完成。发布需要单独授权，不因“完整交付”自行发版。
- 本轮原生后台执行支持 Windows WPF 和 Windows Avalonia；macOS 明确不注册三个后台任务工具，共享逻辑和 UI 门禁仍须验证。
- 终端开关、只读模式、项目目录限制、审批模式和 AppContainer 隔离均保留；Full 模式沿用现有规则，后台任务不增加管理员权限。
- start_job/get_job/stop_job 合同以设计第 5 节为准；默认超时 1800 秒、范围 1–28800 秒，并受项目上限限制；原 exec_command 仍为 1–600 秒。
- 全应用 4、每项目 2 个活动任务，无启动排队；单任务日志 8 MiB、应用日志 128 MiB、缓冲 256 KiB、历史最多 200 个终态且不超过 7 天。
- 启动响应预算 2 秒（扣除授权和沙箱准备）、停止预算 5 秒、正常退出预算 10 秒、流 EOF 收尾最多 2 秒；必须输出分阶段耗时，不能重试掩盖失败。
- 不支持应用退出后常驻，不自动重跑命令，不自动调用模型处理完成事件，不自动放宽网络限制。
- 使用隔离测试数据与本机 fixture；不调用用户日常配置或真实收费模型。Windows 安装/便携仍按现有 WPF 打包，不在本轮增加第二套 Windows 安装内容。

## Review Focus

1. get_job 最小页小于一条日志时必须推进游标，不能重复、丢字或破坏 UTF-8（任务 2）。
2. 同数据目录双实例、恢复尚未结束时不得同时写任务或启动进程（任务 2/6/8）。
3. 起停与删除/迁移/修改根目录并发时，单次 UI 检查不能替代管理器内的所有权锁（任务 6/8）。
4. 共享执行日志不得把 start_job 缓存成新的执行，也不能阻止 get_job 新调用读取实时状态（任务 5）。
5. Avalonia 切换普通聊天/项目会话时，问题交互、审批、草稿、队列与图片交付不能串到另一会话（任务 8/9）。

## 基线、环境和工作区前置检查

初始核对基线：`1bc23755bd9ca9f8492e68be91c6118508446559`。规划过程中另一项工作更新了远端；最终已用 ls-remote 核对新基线 `03170c127d90abdab29ddae2bdf5912027d4055c`，并读取两者差异：生产代码仅涉及现有 PowerShell 启动引导和手动终端，另有文档清理。后续读取固定使用03170c1对象，避免并发变化造成混用。

`PowerShellStartupTests`及`AgentCommandExecutor.PowerShellBootstrap`现已提交，后续复用并回归，不覆盖为旧实现；旧路线图及历史验收文档已被远端清理，不恢复它们。根工作区仍保留原有删除现场；独立副本此时已无未提交改动，本轮不操作其源码。

未来实施前：

- [ ] 重读 AGENTS.md、远端 dev SHA 和当前 dirty 清单；按 using-git-worktrees 建立隔离实现目录，不恢复、移动或覆盖两个现有现场。沿用 codex/ 分支前缀。
- [ ] 将本计划、设计、清单带入实现目录；所有以下相对路径以该目录为根。`git show` 发现接口变化时只修订相关步骤，记录基线差异。
- [ ] 确认 Framework64 csc、Windows PowerShell 5.1、WebView2 和 `desktop/global.json` 要求的 .NET SDK 10.0.402（允许 latestPatch）；缺 SDK 不得把 Avalonia 测试写成通过。
- [ ] 本机确认 AppContainer 测试可运行；不为测试更改防火墙、loopback 豁免或系统权限。UI/进程测试串行占用桌面，避免原生输入被其他任务抢占。

## 文件职责与稳定接口

新增共享源码全部使用 namespace Dafeiyu。共享核心已通过 `AgentKernel/*.cs` 通配收录内核文件；位于 src 根的新 Windows/UI无关文件必须显式加入 `desktop/DeskPet.Core/DeskPet.Core.csproj`，WPF UI 文件不能加入 Core。

| 文件 | 职责 |
| --- | --- |
| src/AgentKernel/AgentJob.cs、AgentJobPolicy.cs | 不可变请求、归属、状态/结果、策略常量、时钟与进程接口 |
| src/AgentKernel/AgentJobStore.cs、AgentJobLogStore.cs | 元数据原子替换、日志/游标、配额、数据目录独占租约、恢复 |
| src/AgentKernel/AgentJobManager.cs | 幂等、容量、启动交接、状态、所有权范围操作、退出 |
| src/WindowsAgentJobProcess.cs | 进程/Job/流/沙箱租约；不持有会话 UI |
| src/AgentKernel/AgentJobTools.cs | schema、规范化参数、显式授权、工具返回 |
| src/PetAgentJobServices.cs、AgentJobCatalogGuard.cs | WPF 服务装配；可被两端复用的删除/移动门禁 |
| src/AgentJobCard.cs、AgentJobsPanel.cs、AgentJobViewState.cs | WPF UI 和可共享的视图状态/通知/预览候选解析 |
| desktop/DeskPet.Desktop/DesktopWorkspaces.cs、WorkProjectPicker.cs | 阶段 B：项目/会话最小绑定；复用 ProjectCatalogStore/ConversationWorkspaceManager |
| desktop/DeskPet.Desktop/AgentJobCard.cs、AgentJobsPanel.cs | 阶段 B 的 Avalonia 控件，消费相同快照 |

锁定以下命名和接口，详细字段按设计第 4–8 节；新增类型在对应任务创建，不引用不存在的后续实现：

```csharp
// 任务 1 定义：所有集合返回快照，不暴露可修改记录或原生句柄。
enum AgentJobState { Starting, Running, Stopping, CleanupFailed,
    Succeeded, Failed, Stopped, TimedOut, StartFailed, Interrupted }
enum AgentJobStopReason { User, Timeout, ApplicationExit, OwnerDeleted, LogStorageFailed }
interface IAgentJobClock { DateTime UtcNow { get; } long ElapsedMilliseconds { get; } }
interface IAgentJobProcess : IDisposable {
    Task<AgentJobProcessExit> Completion { get; }
    void Resume();
    Task<AgentJobCleanupResult> StopAsync(CancellationToken token);
}
interface IAgentJobProcessFactory {
    Task<IAgentJobProcess> PrepareAsync(AgentJobSpec spec, AgentJobPolicySnapshot policy,
        Action<AgentJobOutputChunk> output, CancellationToken token);
}
// 任务 2 创建存储实现；日志配额和独占写入租约由 AgentJobStore 拥有。
// AgentJobOwner = projectId + conversationId + agentId，来自宿主，构造后只读。
// AgentJobStartContext = Owner + 已规范化 invocation + 实时权限/目录复核入口。
// Spec = command/title/真实 cwd/有效 timeout，不接受外部原生进程标识。
// 任务 3 创建管理器；各方法异步，授权使用现有 IAgentAuthorizationService。
Task<AgentJobSnapshot> StartAsync(AgentJobStartContext context,
    AgentJobSpec spec, CancellationToken requestToken);
Task<AgentJobPage> GetAsync(AgentJobOwner owner, string id, string cursor,
    int limitBytes, CancellationToken token);
Task<AgentJobSnapshot> StopAsync(AgentJobOwner owner, string id,
    AgentJobStopReason reason, CancellationToken token);
Task RecoverAsync(CancellationToken token);
Task<AgentJobExitReport> StopAllForExitAsync(CancellationToken token);
// 管理器 internal 宿主接口，绝不能映射成工具 JSON：
IList<AgentJobSnapshot> ListForProject(string projectId);
Task<AgentJobSnapshot> StopForUserAsync(string id, AgentJobStopReason reason,
    CancellationToken token);
```

`AgentJobSnapshot` 包含设计返回字段、不可变 Owner、原始命令/目录、日志警告、CleanupConfirmed；`AgentJobPage` 包含 Snapshot 与有界 records/cursor；`AgentJobProcessExit` 包含主进程退出码和整树完成信息；`AgentJobCleanupResult` 区分清理确认与失败；`AgentJobExitReport` 给出全部任务清理结果。原生 PID 只用于本机诊断，不作为恢复或工具权限依据。

Owner.AgentId 对主工作助手使用宿主固定身份 `primary`，不使用会随build/plan切换的kernelProfile.Id或临时kernelSession ID；只读子worker不继承该绑定。这样原会话进入产品Plan模式后仍可只读get_job，不能因模式切换换身份而丢失记录，也不能借profile名伪造别的Agent。

## Task 1：合同、设置迁移与测试入口

**Files:** 新 AgentJob.cs/AgentJobPolicy.cs；改 `src/Settings.cs`、`src/ProjectCatalog.cs`、`src/ConversationWorkspace.cs:EffectiveSettings`、`src/WorkSettingsPanel.cs:ProjectSettings/ApplyProjectFields/Copy`；新增 `tests/AgentJobContractTests.cs`、`tests/AgentJobTestSupport.cs`；改 Build.ps1、DeskPet.Tests.csproj/PortableTests.cs。

**Consumes:** 现有 PetSettings/ProjectDefinition、Main/Check 自运行测试模式。**Produces:** 上述数据类型/接口；PetSettings.AgentBackgroundJobTimeoutSeconds 和 ProjectDefinition.BackgroundJobTimeoutSeconds，均默认1800、上限28800。

- [ ] 写 `LegacySettingsKeepExecTimeout`、`LegacyProjectDefaultsTo1800`、`CopyAndEffectiveSettingsKeepBackgroundLimit`、`ValidateExactArgumentBounds`。核心断言：`Check(old.AgentCommandTimeoutSeconds==120 && old.AgentBackgroundJobTimeoutSeconds==1800)`；参数0、28801拒绝，1、28800按项目上限处理。
- [ ] 登记 AgentJobContractTests，补共享测试 `--agent-job-contract-only` 并运行，确认因新字段/接口缺失失败，退出码非0；不因语法错误或未登记而冒充业务红测。
- [ ] 实现数据默认值、反序列化初始化、Normalize/Copy及项目字段映射，终端旧600秒上限不变。UI字段与后续任务一起实现，不改其他设置。
- [ ] 运行新合同目标、WorkSettingsTests、ProjectCatalogTests及共享合同入口；验证旧配置反序列化、复制、保存重开仍保持原值。
- [ ] 在隔离实现分支提交本任务文件，消息 `feat: define background job contracts and settings`。以下任务提交同样只纳入自身已验证文件，不 git add .。

## Task 2：元数据、日志游标、配额与恢复

**Files:** 新 AgentJobStore.cs/AgentJobLogStore.cs；新 `tests/AgentJobStoreTests.cs`、`tests/AgentJobLogTests.cs`；登记两个 Build 目标及共享专用/默认入口。

**Consumes:** 任务1的快照/时钟。**Produces:** AgentJobStore(dataDirectory,clock) 独占租约、`SaveAsync(AgentJobSnapshot,CancellationToken)`、`LoadAsync(CancellationToken)`；AgentJobLogStore 的 `Append(AgentJobOutputChunk)`、`ReadAsync(string cursor,int limitBytes,CancellationToken)`、`FlushAsync(CancellationToken)`。后者绑定单 job/日志代次，不接受路径。

- [ ] 写 `AtomicReplaceFailureKeepsPrevious`、`ReparseAndInvalidJobIdRejected`、`SecondWriterDenied`、`PartialTailDoesNotCorruptOtherJobs`、`RecoverNonTerminalAsInterrupted`；断言恢复调用进程工厂0次、不操作旧PID，损坏记录隔离且不重建成功记录。
- [ ] 运行新存储目标确认失败；实现同目录原子写入、版本号、64KiB记录上限、根租约和目录验证。元数据/日志同步用明确锁序，不能拿管理器锁等待磁盘。
- [ ] 写日志用例：`Utf8SplitPreservesChinese`、`MinPageAdvancesWithinLargeRecord`、`ExpiredCursorReportsGap`、`OtherJobCursorRejected`、`TwentyMiBStaysBounded`、`SlowWriterDrainsPipe`、`RetentionUsesFinishedTime`。断言最小1024字节页仍能推进并拼回原文本；8MiB磁盘和256KiB待写队列上限可计数。
- [ ] 实现最多2×4MiB完整记录段（含记录封装字节），UTF-8正文每记录≤8KiB。页限制计正文UTF-8字节；跨页片段附fragment_offset，令牌绑定seq/片段位置/代次；外部不得传裸偏移。轮转、丢弃与恢复缺尾都返回明确缺口。日志读取与轮转并发使用快照，不静默重复。
- [ ] 实现全局128MiB及活动预留、终态200/7天清理（年龄从FinishedUtc计算）、状态即时落盘和每秒索引刷盘；运行全部存储/日志及共享默认入口。用假时钟/受控写入器验证，无需真填满磁盘。提交 `feat: persist bounded background job records and logs`。

## Task 3：管理器状态机、幂等与启动事务

**Files:** 新 AgentJobManager.cs；新 `tests/AgentJobManagerTests.cs`，扩充 TestSupport 的假进程、屏障、授权spy和假时钟；登记共享/Build目标。

**Consumes:** 任务1接口＋任务2存储。**Produces:** 上述管理器方法及 `event Action Changed`，变更仅发送通知，订阅者自行读取快照；任务完成不调用模型。

- [ ] 写 `TenConcurrentStartsLaunchOnce`、`SameKeyChangedPayloadDenied`、`CapacityRecheckedAfterApproval`、`WorkspaceDisposeDoesNotStopCommittedJob`。断言工厂启动次数1；应用4/项目2；幂等先查原记录再检查新启动容量。
- [ ] 写 `CancellationAtEveryHandoffBarrier`、`RunningSaveFailureStopsStartedProcess`、`RootExitDoesNotFinishLiveChildren`、`StopExitRaceHasOneTerminalEvent`、`CleanupFailureKeepsSlot`，先运行确认失败。
- [ ] 实现规格第6节事务：规范化/授权→再验权限与目录→预留/持久Starting→挂起准备→检查取消→Resume/持久Running→原子交接。beforeEffect在可能产生副作用前登记；交接后请求token不再拥有进程。
- [ ] 实现按owner的Get/Stop，UI内部StopForUser，管理器退出屏障，超时与log_storage_failed；清理确认才释放活动名额，终态不因重复取消改写。幂等重放只能查询原记录，不对Interrupted/记录缺失的旧调用自动生成新运行实例；旧任务调用须由宿主执行日志拒绝，不把ExecutionClaimStore.TryClaim当成永久幂等保证。
- [ ] 运行管理器/存储/日志共享入口；用等待屏障而非Sleep覆盖并发，并执行副作用计数断言。提交 `feat: manage background job lifecycle and ownership`。

## Task 4：真实 Windows 进程树与流适配

**Files:** 新 src/WindowsAgentJobProcess.cs；改 Core.csproj 显式包含该文件；如确有必要仅向 WindowsCommandRunner.cs/AgentKernel/WindowsWorkspaceSandbox.cs 提取最小原生辅助；新 `tests/AgentJobProcessTests.cs`、`tests/AgentJobSandboxTests.cs`，TestSupport 增加同程序 fixture 分支及 NativeLauncher；登记Build和.NET共享测试Windows专用入口。

**Consumes:** IAgentJobProcessFactory、策略快照和流回调。**Produces:** WindowsAgentJobProcessFactory；PrepareAsync返回挂起且已入Job的实例，Resume只调用一次，Completion等待整树与流收尾，StopAsync仅操作持有的Job。

- [ ] 在自运行exe中实现隔离fixture模式：wait、exit-code、utf8-fragments、flood、tree、root-exit-child、http-server。只根据测试控制器提供的绝对临时目录和随机标记运行；fixture模式在正常测试参数解析前分发。Main覆盖异常打印及finally清理。
- [ ] 写 `StartReturnsBeforeThirtySecondTask`、`RootExitChildLivesUntilTreeEnds`、`StopKillsTreeNotNamesake`、`AssignmentFailureNeverRunsMarker`、`EofDrainDeadline`、`JobHandleNotInherited`；先运行失败。原生断言用Job活动数和进程创建身份，不只检查单一PID。
- [ ] 实现挂起创建→分配kill-on-close Job→准备异步排流→Resume；句柄默认不可继承，沙箱/授权目录租约持续到整树退出。使用现有干净环境白名单和已提交的PowerShellBootstrap，不新增环境覆盖入口或退回旧SandboxBootstrap。查询Job活动数确认整树结束，不按根进程退出抢先清理子进程。
- [ ] 实现停止/超时/EOF限时收尾；写并验证 `AppContainerBlocksUnapprovedReadWrite`、`LeaseOutlivesWorkspace`、`SandboxFailureNeverUsesHost`，以及连续100任务资源回收。注入原生错误需保留失败阶段/Win32错误码及已清理结果。
- [ ] 运行新原生目标（Framework及.NET10 Windows各一遍）、PowerShellStartupTests、AgentTerminalTests、WindowsTerminalTests、WorkspaceSandboxTests、McpConnectionTests；若动了StartStdio/手动终端公共代码，加TerminalUiTests/McpUiSmoke。提交 `feat: execute background jobs with owned Windows process trees`。

## Task 5：三个工具与稳定会话归属

**Files:** 新 AgentJobTools.cs、`src/ConversationAgentJobs.cs`；改 AgentWorkspace.cs、AgentToolCatalog.cs、AgentKernel/AgentPermission.cs、AgentKernel/AgentAccessPolicy.cs、ConversationWorkspace.cs/Manager.cs、ConversationSession.cs；Core.csproj收录ConversationAgentJobs；新 `tests/AgentJobToolTests.cs`、`tests/AgentJobIntegrationTests.cs`。

**Consumes:** 管理器、ProjectDefinition/ConversationDefinition、现有授权/执行日志。**Produces:** `ConfigureJobs(AgentJobManager,AgentJobOwner)`注入宿主绑定，三工具schema和序列化；会话到工作区只传可信冻结owner，子worker默认不继承启动能力。

- [ ] 写 `ToolSchemasMatchExactLimits`、`SpoofedOwnerIgnoredAndUnknownArgsRejected`、`StartIsShellStopIsJobStop`、`PlanAndDisabledDirectCallsDenied`、`CrossOwnerGetStopReturnSameError`；检查Auto沙箱shell仍人工批准、Full保持原规则。
- [ ] 运行新工具目标确认失败；实现schema、规范化真实cwd及timeout，再创建不可变invocation；start_job专路只授权一次，不能同时经过两次审批；get_job标ReadOnly=true，沿现有只读分支绕过写操作journal的24000字符截断，不能调用工厂；stop_job只触达管理器。最大64KiB日志页须仍为可解析JSON，审计不额外复制完整日志正文。
- [ ] 在ConversationWorkspace.Create/Manager注入project/conversation稳定ID，拒绝Unassigned/Quick/Draft/普通聊天；不从Root或瞬态kernelSession猜归属。每次新Operation读取当前权限版本，旧审批不能沿用。
- [ ] 写并验证 `RealToolCrossTurnContinues`、`LostStartReplyDoesNotRepeat`、`FreshGetCallReadsNewState`、`PlanReadOnlyGetWorks`、`DisabledTerminalStillAllowsOwnedStop`、`CompletionMakesZeroModelCalls`。PlanReadOnlyGetWorks必须真的从build切换plan profile后查询，不能只把一个bool设成只读。使用脚本式IConversationApi驱动真实工具路径，不能只调用Manager即标成集成通过。
- [ ] 运行新工具/集成、AgentApprovalTests、WorkspacePermissionTests、AgentRunRecoveryTests、AgentCodingWorkflowTests；提交 `feat: expose owned background job tools to work conversations`。

## Task 6：WPF应用服务、项目门禁与退出回收

**Files:** 新 PetAgentJobServices.cs、AgentJobCatalogGuard.cs；改 PetWindow.cs:Workspaces/EditProject/DeleteProject/DeleteConversation/MoveConversation、PetImageServices.cs:DrainApplicationForExitAsync、ConversationWorkspace.cs:FollowProject、WorkSettingsPanel.cs、ConversationProjectPicker.cs、ArchivedConversationsPanel.cs；新 `tests/AgentJobLifecycleTests.cs`、`tests/AgentJobProjectTests.cs`。

**Consumes:** 已恢复管理器、项目存储、工作会话。**Produces:** PetWindow.AgentJobs应用服务；共享 `AgentJobCatalogGuard.RunOwnerMutationAsync(projectId,conversationId,Func<Task> mutation,CancellationToken)`，内部按作用域阻止新启动，完成必要停止后才执行mutation；失败不删除/移动。

- [ ] 写 `HiddenWindowAndArchiveKeepJob`、`DeleteStopsBeforeCatalogMutation`、`DeleteCleanupFailureKeepsCatalogAndData`、`MoveOrRootChangeBlockedWhileActive`、`StartRaceCannotEscapeDeleteGuard`；未打开会话也须受门禁保护，不能只看Session.IsBusy。
- [ ] 先运行失败；实现应用唯一服务、数据目录租约和异步恢复。租约失败时只禁用后台任务/关联绑定写入，不影响普通聊天；退出时拒绝新调用和pending审批，所有已有会话共用一个manager。
- [ ] 将目录编辑、项目设置保存、拖动/项目选择器迁移、归档删除全部连到共享guard；拒绝新启动与确认清理在同一门禁作用域。删除调用改为可await核心，不在UI线程GetAwaiter().GetResult等待回收。
- [ ] 写独立应用进程的 normal-exit、tray-exit、update-restart、kill-host、cleanup-timeout 模式：5秒自有树回收、10秒正常退出、异常重启Interrupted、无关进程存活；清理超时保留重试退出入口，不能复用现有退出软deadline直接关闭窗口。
- [ ] 运行新生命周期/项目、ConversationWorkspaceManagerTests、ProjectCatalogTests、SidebarProjectInteractionTests、ConversationArchiveRegressionTests、UpdateApplyTests及既有图像退出回归；提交 `feat: coordinate background jobs with project and app lifecycle`。

## Task 7：WPF卡片、项目任务面板与阶段A验收

**Files:** 新 AgentJobCard.cs/AgentJobsPanel.cs/AgentJobViewState.cs、`src/ConversationAgentJobCards.cs`（ChatMessageView partial）；改 `src/ChatMessageView.cs`、ChatWindow.cs、ChatUnifiedWorkspace.cs、AgentWorkspaceNavigation.cs、ArchivedConversationsPanel.cs；新 `tests/AgentJobUiTests.cs`；Core仅收录AgentJobViewState。复用现有GeneratedImageCard.cs所采用的ChatMessageView partial接入方式，不另建第二个消息渲染入口。

**Consumes:** 不可变快照、Changed、当前可信会话。**Produces:** 原会话任务卡＋项目面板、最多4Hz合并刷新、至多一次完成通知、最多5个无凭据回环候选地址。卡片通过Owner/原调用定位消息，不能凭当前窗口写回。

- [ ] 写 `CardStaysWithOriginalReply`、`PanelFiltersAndSorts`、`PlanCanStopFromUi`、`NewLogsDoNotStealScroll`、`ClosePanelKeepsJobAndDropsSubscriptions`、`TerminalOutcomeNotifiesOnce`；使用真实挂载控件与等待状态条件。
- [ ] 写 `PreviewCandidateIsNotReady`、`NoHttpBeforeClick`、`RejectSchemeExternalHostAndStripCredentials`、`AnsiHtmlAndInstructionsArePlainText`；先运行失败，再实现纯文本日志和显式点击预览。不为候选URL新增网络探测。
- [ ] 实现卡片/面板/设置后台超时字段，复用现有标签打开详情和浏览器；已归档会话也有任务入口。默认查看有界日志页；关闭不取消任务，不向模型追加system/tool消息。
- [ ] 运行浅/深主题、390DIP与宽窗口、隐藏/重开、日志洪泛下≤500ms交互、30秒任务返回≤2秒和原生停止；真实本地预览点击验证fixture正文，停止后端口释放。
- [ ] 对照清单完成阶段A适用全部P0并保存证据，构建WPF App到隔离输出。阶段A提交 `feat: deliver WPF background job workspace`；随后立即任务8，不等待新授权才开始已授权范围的后续实现（当前仍未授权开始实现）。

## Task 8：Avalonia最小持久项目/会话绑定及审批

**Files:** 新 desktop/DeskPet.Desktop/DesktopWorkspaces.cs、WorkProjectPicker.cs、DesktopWorkSessionBinding.cs；改 DesktopController.cs、ChatWindow.cs、WorkSettingsView.cs；新 desktop/DeskPet.Desktop.Tests/AgentJobBindingUiChecks.cs；改UI Program入口；共享新增绑定回归到AgentJobIntegrationTests。

**Consumes:** 同一个ProjectCatalogStore(projects.json)、ConversationWorkspaceManager、AgentJobCatalogGuard及现有审批表面。**Produces:** `DesktopWorkSessionBinding`只读ProjectId/ConversationId/Session/Queue/EffectiveSettings；`DesktopWorkspaces.OpenAsync(projectId,conversationId)`返回绑定；普通聊天保留现有Session，不能用创建临时ID实现绑定。

- [ ] 写 `OrdinaryChatUnchangedWhenUnbound`、`ReopenProjectKeepsStableOwner`、`BindingFailureKeepsOldChatAndFiles`、`SwitchDoesNotRouteApprovalToOtherConversation`、`SharedDataSecondHostCannotWriteBinding`。捕获旧历史文件散列，断言创建工作会话不搬迁/覆盖普通历史。
- [ ] 先运行Avalonia绑定专用入口确认失败；实现最小选择器：选择/新建项目、选择/新建工作会话，必要重命名/归档/删除菜单复用共享模型和guard；不新增WPF同款拖动侧栏。工作目录/权限设置写入所选项目；根变更受活动任务门禁保护。
- [ ] ChatWindow接收可选绑定并持有自己的Session/Queue/EffectiveSettings；所有发送、停止、模型/权限、草稿、听写归属、ask_user、图片交付、导出与清空操作使用该窗口绑定，不能继续直接用全局owner.Session操作另一会话。已有普通聊天仍走原会话。
- [ ] 接入当前项目工作会话的真实审批UI（完整payload/目录/隔离/超时），非当前会话审批显示来源并不串答；未绑定工作入口不注册后台工具。不同窗口同一持久会话复用同一Workspace，不创建两个writer。
- [ ] 运行绑定UI/共享归属、现有Avalonia图片与任务UI、普通聊天/听写/问题交互回归，检查旧历史散列、草稿和队列不串；提交 `feat: bind Avalonia work chats to persistent projects`。

## Task 9：Avalonia任务UI、Windows执行与生命周期

**Files:** 新 desktop/DeskPet.Desktop/AgentJobCard.cs、AgentJobsPanel.cs；改 DesktopController.cs、ChatWindow.cs、WorkSettingsView.cs；新 desktop/DeskPet.Desktop.Tests/AgentJobUiChecks.cs、AgentJobExitChecks.cs；复用共享AgentJobViewState；改UI Program.cs及Core新源收录。

**Consumes:** 任务8绑定＋任务1–7全部共享能力。**Produces:** Windows Avalonia完整后台任务入口；Mac显示不支持，所有JSON直调仍拒绝；Windows执行器不复制第二份。

- [ ] 写 `WindowsBoundHostPublishesJobTools`、`MacAndUnboundDirectCallsDenied`、`OriginalConversationCardSurvivesSwitch`、`WindowCloseKeepsOwnedProcess`；先运行失败。
- [ ] 将应用级manager装配到Windows控制器和所有工作workspace；Session/Queue退出等待枚举全部已打开工作会话。macOS不创建Windows进程工厂，拒绝时不吞成成功或fallback。
- [ ] 用同快照/预览候选/通知逻辑实现Avalonia卡片/面板，≤4Hz更新；完成浅深主题/390DIP/滚动/隐藏/卸载、普通聊天不变和跨项目归属检查。
- [ ] 用.NET10宿主跑真实Windows Job/AppContainer/日志工具集成及用户点击预览；新增独立 normal-exit/kill-host/cleanup-timeout 路径，确认现有ImageTasks退出与Live/Dictation清理没有被跳过。
- [ ] 运行Avalonia默认UI全套和退出专用进程、共享.NET10 Windows原生目标与macOS门禁；提交 `feat: deliver background jobs in Avalonia on Windows`。

## Task 10：登记、最终回归、证据与文档

**Files:** Build.ps1、desktop/DeskPet.Core/DeskPet.Core.csproj、DeskPet.Tests.csproj/PortableTests.cs、DeskPet.Desktop.Tests/Program.cs、.github/workflows/check.yml/macos.yml；新增 `tests/AgentJobRegistrationTests.py`、`docs/agent-background-jobs.md`、`docs/agent-background-jobs-validation.md`；更新README、docs/agent-workflows.md、docs/conversation-agent.md，发布包用户文档清单同步tools/runtime_package.py纳入新使用说明。不要恢复远端已删除的旧路线图/历史验收文件。

**Consumes:** 阶段A/B证据与本清单。**Produces:** 默认测试路径覆盖新能力，最终SHA的可核对验证记录，剩余问题明确保留。

- [ ] 先写登记检查：每个新Build目标在ValidateSet、共享纯逻辑在专用和默认入口、原生Windows入口、双端UI及独立退出进程、CI失败附件路径全部可达；未登记时测试必须失败。
- [ ] 登记测试和CI；原生退出case分独立进程，Windows桌面测试串行；macOS两架构运行共享逻辑/门禁，不运行Windows执行器；任何异常/P0失败退出非0，finally清理不覆盖原始失败。
- [ ] 顺序运行清单的合同/存储/管理器→Windows原生→工具集成→WPF UI/退出→Avalonia绑定/UI/退出→相关旧回归；全部相关通过后只再跑一次默认共享核心及Avalonia默认套件，发生新修改时才补受影响范围。
- [ ] 生成验收表：每个A项、平台、case、SHA、命令、原始日志、实际/预算耗时、进程回收结果；保存失败日志和重跑记录。没有执行的测试标“未运行”，不能把旧CI或假进程结果写成原生通过。
- [ ] 同步使用说明和README TODO：必须阶段A/B各自P0都通过才勾选本轮整体交付；A31单列P1，不可隐藏失败。版本号/标签/推送/Release均按实施时明确授权范围；提交 `docs: document and verify background job delivery`。

## 自查与完成边界

- [ ] 设计每一条A01–A32均在测试清单中有对应suite/case，平台差异明确。
- [ ] 五个Review Focus分别有任务2/5/6/8中的具名测试；接口命名/字段在两端保持一致。
- [ ] 阶段A完成后有必做的阶段B，未将用户“立即接入”写成可选或未来版本。
- [ ] 当前只落地文档；实现前重核最新远端和本机环境。本文命令中的新目标/开关是计划新增，不是已经存在或已经通过。

推荐后续在同一会话顺序执行：核心/原生/工具依赖紧密，先完成并验收WPF，再复用共享实现接Avalonia。用户目前明确要求不改代码，所以本轮停在计划交付，不启动实施。
