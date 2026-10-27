# Agent 后台任务测试执行清单

状态：计划清单，尚未新增或运行下列后台任务测试。设计已认可；先 WPF 完整验收，随后立即 Avalonia Windows 接入，最后形成两阶段总验收。

依据：[实施计划](2026-10-23-agent-background-jobs.md)、[设计 A01–A32](../specs/2026-10-23-agent-background-jobs-design.md)。代码核对基线03170c1；执行时重新记录完整SHA。

## 1. 测试目标和入口合同

所有新增suite沿用项目现有自运行Main/Check风格：成功退出0、失败非0；输出suite名和实际断言数。不能填一个预设通过数。共享默认入口必须调用纯逻辑测试，不能仅通过专用开关才能运行。

| 新增测试文件/Build目标 | 内容 | 共享.NET10登记 |
| --- | --- | --- |
| tests/AgentJobContractTests.cs | 参数、默认值、迁移、Copy、项目配置 | 默认＋--agent-job-contract-only |
| tests/AgentJobStoreTests.cs | 原子写入、恢复、数据目录租约、配额 | 默认＋--agent-job-store-only |
| tests/AgentJobLogTests.cs | UTF-8、分页、轮转、丢弃、刷新边界 | 默认＋--agent-job-log-only |
| tests/AgentJobManagerTests.cs | 假进程状态机、容量、竞争、归属、目录变更门禁 | 默认＋--agent-job-manager-only |
| tests/AgentJobProcessTests.cs | 挂起启动、真实进程树、停止、真实流和资源 | Windows专用--agent-jobs-native-only；不进入macOS执行 |
| tests/AgentJobSandboxTests.cs | 真实AppContainer、授权目录租约、失败不降级 | Windows专用--agent-jobs-native-only |
| tests/AgentJobToolTests.cs | schema、审批、门禁、工具JSON输出、记录策略 | 默认＋--agent-job-tools-only，使用假进程 |
| tests/AgentJobIntegrationTests.cs | 脚本式模型驱动真实会话/工具路径，含真实Windows进程 | RunPortable进入默认＋--agent-job-integration-only；RunNative进入Windows专用，不能用RunPortable替代 |
| tests/AgentJobLifecycleTests.cs | WPF独立宿主退出、更新、崩溃和超时 | 仅WPF Build目标，禁止被误收录Core |
| tests/AgentJobProjectTests.cs | WPF项目/会话归档、删除、迁移、设置和任务竞争 | 仅WPF；共享guard本身另由ManagerTests假进程验收 |
| tests/AgentJobUiTests.cs | WPF卡片/面板/响应性/浏览器预览 | 仅WPF |
| tests/AgentJobRegistrationTests.py | Build/共享默认入口/专用开关/CI/附件登记 | python；静态登记检查不能证明行为正确 |

TestSupport.cs不是独立目标，不定义第二个Main；Build.ps1只向新AgentJob目标额外传入该辅助源。PortableTests.cs必须在参数解析前处理受限`--agent-job-fixture`模式，使.NET10自启动fixture不误跑完整测试套件。

聚合开关：`--agent-jobs-only`运行全部纯逻辑suite；`--agent-jobs-native-only`仅Windows运行Process/Sandbox/Integration.RunNative，在不支持平台显式退出不支持（不能输出native passed）。Mac CI只运行纯逻辑及UI能力门禁。

新增Avalonia入口：

| 代码位置 | 计划新增开关 | 要求 |
| --- | --- | --- |
| desktop/DeskPet.Desktop.Tests/AgentJobBindingUiChecks.cs | --agent-job-binding-ui-only | 项目/会话绑定、普通历史保护、审批归属、未绑定拒绝 |
| 同目录AgentJobUiChecks.cs | --agent-job-ui-only | Windows真实工具/任务UI；Mac执行能力门禁和控件快照，不能宣称本地执行通过 |
| 同目录AgentJobExitChecks.cs | --agent-job-exit-ui-only | 独立进程正常退出，Windows验证子树清理 |
| 同上 | --agent-job-held-exit-ui-only | 注入清理未确认，保留窗口且可重试；不假成功 |
| 同上 | --agent-job-kill-host-ui-only | 控制器强制结束独立宿主，检查子树回收和恢复 |

默认Avalonia套件登记非退出UI/绑定检查；真正退出/kill各开独立进程，不能在同一个已Shutdown的Application里继续测试。macOS两架构不运行Windows进程fixture；应验证工具缺失、直调拒绝和不构造Windows工厂。

## 2. A01–A32逐项追踪

“阶段A”指WPF，“阶段B”指Avalonia Windows；Core/Native共享一次实现，两种目标运行时分别验证。P0无缺项才能宣布该阶段完成；所有未勾选均代表未验证，不能因为文档有条目就勾选。

| 验收 | 指定suite / case | 判据及原始证据 | 状态 |
| --- | --- | --- | --- |
| A01 | Process.StartReturnsBeforeThirtySecondTask；两端集成StartReturnTiming | 授权/沙箱准备/Resume/返回分段计时；返回≤2秒且30秒任务仍活着 | [ ] |
| A02 | WPF UI.ResponsiveWhileJobRuns；Avalonia UI同名 | 长任务期间第二轮/切会话/读日志成功；固定交互≤500ms，记录实际延迟 | [ ] |
| A03 | Integration.RealToolCrossTurnContinues | 第1轮workspace Dispose后进程存活，第2轮同owner get成功；不得新建owner绕过 | [ ] |
| A04 | Process.ActualExitCode；两端UI.TerminalStateArrives | exit0与exit7区分；2秒内可见，完整主进程/整树结束时间 | [ ] |
| A05 | Manager.TenConcurrentStartsLaunchOnce；Integration.LostStartReplyDoesNotRepeat | 10并发副作用计数1；参数冲突拒绝；响应丢失、重开仍不重跑 | [ ] |
| A06 | Tools.PlanAndDisabledDirectCallsDenied / NonProjectAndQuickDenied | 工具schema及伪造直调均验证；拒绝fixture标记文件不存在 | [ ] |
| A07 | Tools.ApprovalPayloadFrozen；Manager.RecheckPolicyAndDirectoryIdentity | Auto沙箱人工、Full保持规则；批准后变命令/cwd/timeout/policy/目录身份均不能用旧授权启动 | [ ] |
| A08 | Tools.CrossOwnerGetStopReturnSameError | 项目/会话/agent逐个改变，均与随机未知ID相同错误；UI仅操作可信原项目任务 | [ ] |
| A09 | Process.StopKillsTreeNotNamesake | 父/子/孙5秒内全部消失；同名无关进程存活；重复停止不改终态 | [ ] |
| A10 | Process.RootExitChildLivesUntilTreeEnds | root_exited=true但state=Running；子树清空才终态；保留root_exit_code | [ ] |
| A11 | Process.AssignmentFailureNeverRunsMarker / CancelSuspended | 创建挂起后故障/取消，副作用文件不存在；自有句柄和活动名额0 | [ ] |
| A12 | Process.TimeoutKillsTree；Manager.StopExitRaceHasOneTerminalEvent | 3秒超时明确TimedOut；取消/自然结束竞态通知≤1次，不伪造自然退出码 | [ ] |
| A13 | Manager.CancellationAtEveryHandoffBarrier＋Integration | 审批/创建/Resume/Running落盘/返回各屏障：交接前回收，交接后保留，记录可查 | [ ] |
| A14 | Manager.RunningSaveFailureStopsStartedProcess；Store.StartingFailure | Starting无法持久不启动；Resume后保存失败杀树、明确可能已执行、不自动重跑 | [ ] |
| A15 | Logs.Utf8SplitPreservesChinese / MinPageAdvancesWithinLargeRecord | 同时stdout/stderr、长行无换行、UTF-8逐字节；页内正文≤limit、每次推进、拼接准确 | [ ] |
| A16 | Logs.TwentyMiBStaysBounded / SlowWriterDrainsPipe | 输出≥20MiB；含封装的日志文件≤8MiB、缓冲≤256KiB；旧游标过期及gap可见，无死锁 | [ ] |
| A17 | Store.ReparseAndInvalidJobIdRejected；Logs.OtherJobCursorRejected | ../、绝对路径、目录链接、伪造/跨job/错代次游标均拒绝，不读任意文件 | [ ] |
| A18 | Manager.CapacityRecheckedAfterApproval / CleanupFailureKeepsSlot | 应用第5/项目第3个无进程；批准后满额拒绝、不缓存批准；清理失败占名额 | [ ] |
| A19 | Store.RetentionUsesFinishedTime / QuotaReservesActiveJobs | 注入7天、200项、128MiB边界；活动任务日志不被清理，启动预留8MiB | [ ] |
| A20 | Manager.LogStorageFailureStopsJob | 注入磁盘满/拒写，不真填磁盘；reason=log_storage_failed，清理失败如实显示，不拖累其他job | [ ] |
| A21 | Lifecycle.NormalTrayUpdateExit；Avalonia Exit.Normal | 三条WPF退出/更新路径，阶段B正常退出；拒绝新启动，清理≤10秒；超时窗口保留 | [ ] |
| A22 | Lifecycle.KillHostRecoversTree；Avalonia Exit.KillHost | 独立控制器杀宿主，5秒内树回收；无关进程存活；重开Interrupted且执行计数不增 | [ ] |
| A23 | Store.PartialTailDoesNotCorruptOtherJobs / RecoverNonTerminalAsInterrupted | 损坏/半尾/旧非终态/旧PID已复用；隔离、告警、不附着不误杀不重跑 | [ ] |
| A24 | Project.DeleteStopsBeforeCatalogMutation / StartRaceCannotEscapeDeleteGuard；Avalonia Binding对应case | 归档可停止；删除失败目录/目录模型不变；移动/改根受锁内门禁；关终端仍可UI停 | [ ] |
| A25 | Sandbox.AppContainerBlocksUnapprovedReadWrite / LeaseOutlivesWorkspace | 原生AppContainer主/子进程越界失败；授权目录租约保留；失败无宿主fallback | [ ] |
| A26 | ViewState.PreviewCandidateIsNotReady / RejectSchemeExternalHostAndStripCredentials | 假URL仅候选；HTTP计数0；最多5项；不泄露query/userinfo，不输出Ready | [ ] |
| A27 | WPF UI.LocalPreviewClick；Avalonia UI.LocalPreviewClick | 宿主静态服务真实浏览器正文匹配fixture；停止端口释放；沙箱被阻时原样报错 | [ ] |
| A28 | 两端UI.ThemeNarrowScrollAndDetach | 浅深/390DIP/宽窗截图、原图无敏感数据；不抢滚动；面板卸载订阅回基线，任务继续 | [ ] |
| A29 | Integration.CompletionMakesZeroModelCalls；ViewState.TerminalOutcomeNotifiesOnce | 模型/联网spy新增0；重复事件通知≤1；get只返回请求页，不隐式追加聊天 | [ ] |
| A30 | Avalonia Binding.WindowsBoundHostPublishesJobTools / MacAndUnboundDirectCallsDenied | 阶段A不宣称B完成；B Windows真执行、未绑定拒绝；Mac双架构共享/门禁通过 | [ ] |
| A31（P1） | Process.HundredShortJobsReleaseResources | 100次后存活0、活动名额0、适配器持有句柄0，订阅无持续增长；单列资源证据 | [ ] |
| A32 | 两端UI.AnsiHtmlAndInstructionsArePlainText | HTML/ANSI/伪工具指令仅文本，无脚本、shell、网络或自动工具调用 | [ ] |

附加回归（计划自查新增，不替代A项）：

- [ ] F01：同数据目录第二manager/第二宿主拿不到写入租约；普通聊天仍可用，任务/项目绑定目录散列不变。
- [ ] F02：元数据恢复完成之前start_job被拒绝；应用退出同时到达的恢复任务不能重新开放启动。
- [ ] F03：最大64KiB get页返回合法完整JSON，ReadOnly路径没有24000字符journal截断；新call_id读到新状态。
- [ ] F04：WorkSettings中后台时限修改独立于同步命令；旧配置、Copy、项目EffectiveSettings、重开同值。
- [ ] F05：Avalonia旧普通聊天历史散列不变、图片/问题/听写/草稿/队列/权限不串会话；项目切换后审批来源清楚。
- [ ] F06：保留最新PowerShellStartupTests；后台命令复用PowerShellBootstrap，不引入模块自动发现冷启动回归。

## 3. 原生fixture与失败诊断

原生测试不能用Sleep猜进程已经准备好：子fixture写随机nonce的ready文件/管道事件，控制器收到后再计停止/崩溃预算。测试父子树还要记录可验证的创建身份，不能因PID已复用误报存活或误杀。

- [ ] 控制器创建独立测试数据根，名称含中文和空格；不覆盖用户生产数据。
- [ ] 固定fixture modes：wait、exit-code、utf8-fragments、flood、tree、root-exit-child、http-server；所有端口由系统动态分配，地址只能回环。
- [ ] 自启动Framework exe与.NET10 apphost/dotnet DLL分别使用正确启动参数；禁止字符串拼接手写未转义命令行。
- [ ] 清理使用自身持有Job/进程身份，不按进程名批量结束；finally记录清理结果，不吞原异常。
- [ ] 子进程异常、stdout/stderr EOF与读句柄关闭均能打断等待；故障注入不进入生产开关或安装包。

每个失败记录：case、SHA、OS/Framework/SDK/进程架构、阶段、异常类型、Win32码（如有）、预算/实际耗时、job/owner（测试ID）、state/root_exit_code、Job活动数、流状态、日志游标/配额/尾部、清理结果；UI追加可见状态和截图。审批输入和fixture输出可保存，不能读取日常Key/代理凭据。不得只保留“timeout”或一个❌。

## 4. 计划执行命令

以下是未来新增目标登记后，在隔离实现仓库根目录执行的命令；本轮不执行。新开关/目标尚不存在。所有目标串行；每次外部程序非0立即记录失败并停止该轮，禁止只看最后一个命令退出码。

WPF/Framework专用目标（按顺序逐项执行并保存独立日志）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobContractTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobStoreTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobLogTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobManagerTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobProcessTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobSandboxTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobToolTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobIntegrationTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobProjectTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobLifecycleTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target AgentJobUiTests
python .\tests\AgentJobRegistrationTests.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target App -OutputDirectory .\work\agent-jobs-validation\wpf-app
```

生命周期目标作为控制器，在内部用不同宿主进程依次执行normal/tray/update/kill/held case；每个case独立超时与日志，不能把退出Application当作suite成功。UI与生命周期的根服务都使用注入测试目录，不启动已安装的日常DeskPet。

共享.NET10（必须在desktop/global.json生效的目录构建；检查各命令退出码后再继续）：

```powershell
Push-Location .\desktop
dotnet restore .\DeskPet.Tests\DeskPet.Tests.csproj --locked-mode
dotnet build .\DeskPet.Tests\DeskPet.Tests.csproj -c Release -m:1 --no-restore
Pop-Location
dotnet .\desktop\DeskPet.Tests\bin\Release\net10.0\DeskPet.Tests.dll --agent-jobs-only
# 以下仅Windows，执行真实.NET10 Job/AppContainer与工具进程：
dotnet .\desktop\DeskPet.Tests\bin\Release\net10.0\DeskPet.Tests.dll --agent-jobs-native-only
# 最终默认入口回归，保留结构化报告：
dotnet .\desktop\DeskPet.Tests\bin\Release\net10.0\DeskPet.Tests.dll --report-json .\work\agent-jobs-validation\portable-report.json
```

Avalonia阶段B：

```powershell
Push-Location .\desktop
dotnet restore .\DeskPet.Desktop.Tests\DeskPet.Desktop.Tests.csproj --locked-mode
dotnet build .\DeskPet.Desktop.Tests\DeskPet.Desktop.Tests.csproj -c Release -m:1 --no-restore
Pop-Location
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll --agent-job-binding-ui-only
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll --agent-job-ui-only
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll --agent-job-exit-ui-only
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll --agent-job-held-exit-ui-only
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll --agent-job-kill-host-ui-only
# 最终默认UI回归：
dotnet .\desktop\DeskPet.Desktop.Tests\bin\Release\net10.0\DeskPet.Desktop.Tests.dll
```

UI检查可以使用现有Avalonia headless驱动，但Windows进程执行/退出/浏览器证明必须是真实实现，不用headless截图替代进程证据。macOS CI继续使用现有RID/架构步骤运行默认共享及非执行UI门禁，不运行上面的Windows native-only。

## 5. 旧功能回归选择

采用现有 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target <目标名>`。下表每个目标均已在03170c1存在；具体运行按变更范围，改动再次影响时才重跑。

| 触及范围 | 必跑旧目标 |
| --- | --- |
| PowerShell/进程/隔离 | PowerShellStartupTests、AgentTerminalTests、WindowsTerminalTests、WorkspaceSandboxTests、AgentIsolationTests、McpConnectionTests |
| 授权/工具记录 | AgentApprovalTests、WorkspacePermissionTests、AgentRunRecoveryTests、AgentCodingWorkflowTests、AgentKernelIntegrationTests |
| 目录/会话/删除迁移 | ProjectCatalogTests、ConversationWorkspaceManagerTests、ConversationOwnershipTests、ConversationArchiveRegressionTests、SidebarProjectInteractionTests、WorkSettingsTests |
| WPF任务卡/标签/预览 | UnifiedWorkspaceTests、UnifiedPreviewTabsTests、ConversationViewportTests、ThemeTests、BrowserPaneTests、TerminalUiTests |
| App退出/更新/并行图像服务 | UpdateApplyTests、ImageTaskCenterLifecycleTests、ImageTaskCenterUiTests；Avalonia既有--image-writer-exit-ui-only、--image-held-exit-ui-only、--image-exit-ui-only独立进程 |
| Avalonia会话绑定 | 默认共享和Avalonia套件；原图片UI/任务UI入口、问题交互、普通聊天历史/草稿/队列、语音所有权与退出检查 |

真实UI焦点被其他桌面操作抢占须保留原失败，查明case实际到达阶段；不能用跳过鼠标或延长所有超时将整轮写成通过。AppContainer受环境拒绝时记录环境阻断，不能降级宿主执行代替沙箱断言。

## 6. 阶段门槛与证据格式

### 阶段A：完整WPF

- [ ] Task1–7完成；A01–A29/A32的WPF适用P0均通过，A30的“未接入Avalonia不虚报”通过，A31单列结果。
- [ ] 新共享逻辑、真实Framework和.NET10 Windows进程适配通过；WPF正常/托盘/更新/强制结束四路径可复核，无遗留fixture。
- [ ] 真实构建/测试/本地预览三种工作负载验证：模型用脚本夹具，进程真实；能读日志、退出码、停止和浏览器正文。
- [ ] WPF阶段证据保存后立即进入阶段B，不停止在“之后可接入Avalonia”的说明。

### 阶段B：立即接入Avalonia

- [ ] 绑定/审批/草稿/普通聊天历史保护、所有适用A项在Avalonia Windows通过；Windows真实原生/退出/预览不能只复用A阶段截图。
- [ ] macOS Intel/Apple Silicon共享逻辑/能力门禁通过；明确不支持macOS本地后台执行。
- [ ] 默认共享/Avalonia测试路径及失败附件登记检查通过，源码构建成功。

### 最终完成

- [ ] 阶段A/B全部P0通过；P1如失败明确记录影响和处理，不隐去。
- [ ] 无用户生产数据、密钥或外部收费调用；测试fixture及临时进程清理完成。
- [ ] 验收记录区分本机、远端CI、真实浏览器、平台；当前没有发布授权，不创建Release或改Windows发行内容。

建议将本地证据存入忽略的 `work/agent-jobs-validation/<sha>/`，保留 suites.json、acceptance.json、每suite日志、每退出进程日志、UI截图与fixture运行清单。acceptance.json每项至少有 id/platform/suite/case/status/sha/command/duration_ms/budget_ms/log/cleanup_confirmed；未运行为not_run，不填passed。

将摘要写入新的 `docs/agent-background-jobs-validation.md`，不恢复已清理的历史报告。CI应在失败时上传同次SHA证据；未获推送授权时只报告本地结果，不把未来CI写成通过。
