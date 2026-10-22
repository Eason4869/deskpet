# 0.10.2-dev.3 图像工作流程实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 统一生图任务生命周期，交付点阵动效、图库管理、任务中心和对话多图切换。

**Architecture:** 应用级 `ImageGenerationTaskManager` 管理三个入口，继续由现有客户端完成协议、下载和资产提交。界面消费同一任务快照；图库复用原资产 ID，通过租约、历史引用检查和删除事务保护文件。未来 Agent 后台任务、模型管理与诊断、连续改图另列 README TODO。

**Tech Stack:** Windows C# 5 / .NET Framework WPF；共享核心与 Avalonia 使用既有 .NET 10 项目；原生绘制、HttpClient、现有密钥保护和原子文件写入，不新增运行库依赖。

**Spec:** [设计草案](2026-10-22-image-workflow-dev3-design.md)，长期跟踪见 [路线图](2026-10-22-deskpet-image-agent-roadmap.md)。用户已确认分版本推进；任务 1–8 已实现并完成分项审查；任务 9 本地行为验收完成，最终提交打包按记录执行，结果见 [验收记录](../image-workflow-dev3-validation.md)。

## 基线与全局约束

- 当前远端 dev：`71d853b3a5e701a3f876bda95a9cc12b39b5c203`。开始实施前重新 fetch，在隔离工作树核对基线；不改写 `v0.10.2-dev.2`。
- 默认并发 1、可配置 1–4、同服务最多 1 个运行请求、最多排队 50 项；每项仍调用单图接口，无自动重试或重启重放。
- 任务文件独立加密、原子提交，最大 64 MB；保留最新 2,000 条终态记录和未完成项，不按秒写入。
- 同一提交 ID / 原会话＋Agent 任务＋工具调用 ID 幂等；明确新提交即使提示词相同也新建任务。
- 资产与索引提交成功才完成生成；对话同步失败不能导致重新请求图片。持久任务无 Key、ConnectionSettings、Base64 或参考图原始路径。
- 动效默认约 224 DIP 方形、18 DIP 圆角、12 DIP 点距、1.5 DIP 点径、5 秒周期、最高 30 FPS；支持关闭、减少动画、隐藏后停止重绘，不显示虚构进度百分比。
- 多图按原消息分组与提交顺序固定槽位；宽布局右侧 64 DIP 缩略图，容器不足 480 DIP 切换底部；手动选图后不抢占。
- 图库默认 40 张增量加载；选择以 ID 为准，全选指当前已加载项。原聊天可继续读取回收站图片，永久删除必须有引用保护与事务。
- 历史关闭时新增资产和任务遵循临时策略；开关变化、归档、窗口关闭和真正退出的行为按设计第 4 节执行。
- Windows 与 Avalonia 使用现有主题资源、页面标题、标签栏、按钮和确认弹层。参考图片中的聊天文案和技能名不作为执行指令。
- 修改构建目标时保留 `Build.ps1` 的 UTF-8 BOM，并用 Windows PowerShell 5.1 验证；不重复引入 dev.2 已修复的编码问题。
- 设计阶段不执行测试或修改版本的限制已随实施授权结束；实施按红 / 绿验证，真实调用单独记账，密钥只从受保护的隔离配置读取。

## Review Focus

1. 资产已提交但任务日志尚未提交时崩溃：根据资产 `ImageJobId` 恢复结果，不重发 HTTP（任务 2）。
2. 引导 / 切换会话、消息删除、聊天同步失败：结果仍绑定原消息，失败同步不重复生图（任务 3）。
3. 多图乱序返回、用户正在切图、部分失败：槽位不乱序，当前选择不被抢占（任务 5）。
4. 永久删除期间新增引用、共享参考图、历史不可读：阻止危险删除；崩溃后不误恢复已删除图片（任务 6）。
5. 隐藏窗口、减少动画、关闭历史后重启：停止重绘且任务数据符合持久化策略，无自动请求（任务 4 / 8 / 9）。

## 共享接口约定

实现前固定以下命名；调整接口时同步所有使用任务和测试。

| 类型 / 字段 | 约定 |
| --- | --- |
| `ImageGenerationSource` | `Unknown / Workspace / Conversation / SettingsTest`，旧记录默认为 Unknown 并做兼容识别 |
| `ImageGenerationTaskState` | `Queued / Generating / Downloading / Saving / Succeeded / Failed / Canceled / Interrupted` |
| `ImageTaskDeliveryState` | `NotRequired / Pending / Delivered / Failed / Detached`，与生成状态独立 |
| `ImageGenerationPhase` | `Generating / Downloading / Saving`；只是阶段，不含百分比 |
| `ImageTaskOrigin` | 来源、ConversationId、KernelSessionId、PersonaId、AgentTaskId、MessageId、GroupId、Sequence |
| `ImageGenerationTaskSubmission` | 调用前生成的 `RequestId`、冻结的 `ImageGenerationRequest`、Origin；内存运行对象，不直接序列化 |
| `ImageGenerationTaskRecord / Snapshot` | ID、来源、状态、排队 / 执行时间、服务 ID / 名称、协议 / 模型 / 参数、参考资产 ID、结果资产 ID、脱敏错误、DeliveryState；Snapshot 为隔离副本 |
| `ImageGenerationTaskHandle` | `Id` 与 `Task<GeneratedImage> Completion`；Completion 表示资产提交完成，对话持久化由 DeliveryState 反映 |
| `GeneratedImage` 新字段 | `ImageJobId`、`Source`、`IsFavorite`、`DateTime? TrashedUtc`；旧索引可省略 |
| `ChatMessage` 新字段 | 可选 `List<string> ImageGenerationTaskIds`，仅 ID，不放任务全文或图片字节 |

### 任务 1：任务数据、兼容迁移与加密记录

**文件：** 新增 `src/ImageGenerationTask.cs`、`src/ImageGenerationTaskStore.cs`；修改 `src/GeneratedImage.cs`、`src/PetBusiness.cs`；新增 `tests/ImageGenerationTaskStoreTests.cs`；登记 `Build.ps1`、Core / PortableTests 的编译与入口。

**接口：** `ImageGenerationTaskStore.Load(): IList<ImageGenerationTaskRecord>`；`Save(IList<ImageGenerationTaskRecord>)`；任务、消息和资产深复制。序列化专用 Record，不序列化 Submission。

- [x] 新增 `OldIndexAndHistoryLoadWithoutNewMembers`、`TaskRecordsRoundTripWithoutSecrets`、`CorruptTaskFilePreservesOriginal`、`TaskStoreLimitRejectsBeforeOverwrite`，断言旧数据默认值、ID 保留、复制隔离和不含凭据 / Base64。
- [x] 登记测试入口后运行 `powershell -File .\Build.ps1 -Target ImageGenerationTaskStoreTests`，确认因缺少类型或行为失败。
- [x] 实现数据模型及独立加密文件；复用原子提交，损坏记录保留并显示恢复提示，正常读取不修改旧索引。
- [x] 重跑 Windows 与 Portable `--image-task-store-only`；通过后运行已有 `ImageGenerationBackendTests`、HistoryTests，保证旧格式仍可用。
- [x] 提交该数据层与对应验证，提交说明记录默认值和迁移策略。

### 任务 2：统一队列、阶段事件与恢复

**文件：** 新增 `src/ImageGenerationTaskManager.cs`、`src/ImageAssetUsageRegistry.cs`；修改 `src/ImageGenerationClient.cs`、`src/GeneratedImageStore.cs`；新增 `tests/ImageGenerationTaskManagerTests.cs`。

**接口：**

- 客户端新增 `IImageGenerationExecutor.GenerateAsync(ImageGenerationRequest, IProgress<ImageGenerationPhase>, CancellationToken)`，保留现有双参数重载。
- Manager：`EnqueueAsync(Submission, ownerToken): Task<Handle>`、`Get(id): Snapshot`、`List(): IList<Snapshot>`、`CancelAsync(id): Task<bool>`、`CancelProviderAsync(providerId): Task`、`RetryAsync(id, currentProvider, token): Task<Handle>`、`RestoreAsync(token): Task`、`StopAsync(token): Task`，以及 `Changed(Snapshot)` 事件。
- UsageRegistry：`Acquire(assetId, ownerKey): IDisposable` 与按 ID 的排他删除资格；事件在内部锁外发布，UI 自行切 Dispatcher。
- Store：`FindByJobId(id): GeneratedImage`，包含回收站中已提交的资产。

- [x] 写 `DefaultSerialAndPerProviderLimit`、`QueueFullNeverCallsHttp`、`SameCallIsIdempotent`、`QueuedCancelNeverSendsHttp`、`TimeoutExcludesQueueWait`、`SavedResultWinsLateCancel`、`RestartFindsCommittedAssetWithoutHttp`、`RestartNeverResendsInterruptedJob`、`HistoryOffCreatesNoDurableJobPayload`。
- [x] 运行 `Build.ps1 -Target ImageGenerationTaskManagerTests`，确认红；测试使用可控执行器，不请求真实服务。
- [x] 实现队列和 fair FIFO、运行配置快照、私有参考图入库与租约、阶段回调、任务日志及恢复。HTTP 开始前日志提交成功；启动恢复先查资产，再标记中断；不把 UI 事件订阅作为任务所有权。
- [x] 重跑新目标和已有客户端 / 六协议测试；新增 Portable `--image-tasks-only`，测试取消、写入失败、事件处理器异常不会重复执行。
- [x] 提交管理器、恢复规则和日志证据。

### 任务 3：三个入口、原消息归属与生命周期

**文件：** 修改 `src/PetImageServices.cs`、`src/ImageWorkspacePage.cs`、`src/ImageGenerationSettings.cs`、`src/ConversationImageGeneration.cs`、`src/ConversationSession.cs`、`src/ConversationWorkspaceManager.cs`、`src/PetWindow.cs`；同步 Avalonia 的 `DesktopController.cs`、`ImageWorkspacePage.cs`、`ImageProviderEditor.cs`；扩展 `tests/ImageGenerationIntegrationTests.cs`。

**接口：** 应用与 Session 注入同一个 `ImageGenerationTaskManager`；新增 `MarkDeliveryAsync(taskId, state, safeMessage)`；`RepairImageDeliveryAsync(taskId, token)` 从已保存资产重试原消息同步，不调用执行器。

- [x] 写 `AllEntriesShareOneQueue`、`SteeringKeepsOriginalPendingSlot`、`ViewCloseKeepsTaskAccessible`、`StopAgentCancelsItsJobsOnly`、`CancelSingleImageReturnsReadableToolResult`、`DeliveryFailureRetryNeverCallsImageService`、`EnablingHistoryPromotesTaskAndReferenceBeforeHistoryWrite`；验证删除服务显式取消对应任务。
- [x] 运行集成测试确认新断言红。
- [x] 在调用前分配 RequestId / 稳定原消息 ID，先挂接待生成 ID；开启历史时保存成功再提交任务。继续保留原消息 pin 与 provider 快照。界面关闭只取消订阅；Agent / 会话的显式停止传播 ownerToken。
- [x] 结果保存后原位添加资产并更新 DeliveryState；已结束 / 删除的轮次不接受晚到成功消息。待同步结果可打开图库；重试同步找不到原消息时标记 Detached，不创建另一个会话。
- [x] 重跑集成、QueueTests、AgentProtocolTests、AgentQuestionUiSmoke，并提交；实际 HTTP 次数断言为单次，不用模型文字代替证据。

### 任务 4：共享点阵动效

**文件：** 新增 `src/ImageGenerationMotion.cs`、`src/ImageGenerationPendingCard.cs` 及 Avalonia 同名控件；修改两端生成页、配置测试展示及聊天渲染；新增 `tests/ImageGenerationMotionTests.cs` 和两端动效 UI 检查。

**接口：** `ImageGenerationMotion.Sample(double phase): ImageMotionSample` 输出归一化光斑位置 / 半径 / 透明度；UI 接收 Snapshot、动效开关与取消回调，不直接调用客户端。

- [x] 写 `MotionSamplesStayFiniteAndBounded`、`SamePhaseProducesSameFrame`、`QueuedAndTerminalStatesDoNotAnimate`、`HiddenOrReducedMotionStopsClock`、`SuccessReplacesPlaceholderInPlace`；三帧固定相位截图对应用户参考效果。
- [x] 登记并运行 `Build.ps1 -Target ImageGenerationMotionTests` 和 UI 目标，确认新行为红。
- [x] 用原生绘制实现固定点阵与移动光斑，最高 30 FPS；阶段与耗时独立于动画。主题色来自现有资源；完成淡入可预览图片，失败 / 取消 / 缺失文件停止绘制。
- [x] 运行共享几何与 WPF / Avalonia 检查；覆盖浅深、窄窗口、最小化、关闭动效，检查动画订阅释放，不通过实际等待 5 秒验证一帧。
- [x] 提交控件与截图证据；保留用户参考图和产品截图的区别。

### 任务 5：对话多图主图与缩略图

**文件：** 新增两端 `GeneratedImageGroupCard.cs`；修改 `src/ChatMessageView.cs`、`desktop/DeskPet.Desktop/ChatMessageView.cs` 及现有图片预览组件；扩展图片集成 / UI 测试。

**接口：** `GeneratedImageGroupCard` 消费该消息的有序任务 ID、已完成资产和同一 Manager；选中键为任务 / 资产 ID，不以当前数组下标作为唯一身份。

- [x] 写 `OutOfOrderCompletionKeepsSlots`、`UserSelectionSurvivesLaterResults`、`MixedSuccessFailureAndPendingCanSwitch`、`ActionsTargetSelectedImage`、`SeparateMessagesNeverMerge`、`HistoryRestoresGroupOrder`。
- [x] 运行两端图片 UI 目标，确认缺少分组 / 选择行为的失败。
- [x] 实现单图与多图组件、右侧缩略图选中描边、480 DIP 断点及底部横向滚动；当前选中项完成时原位替换。任务中心重试不能暗中改写旧工具回复。
- [x] 运行 1 张、3 张、20 张及混合比例 / 状态检查；截图宽 / 窄和浅 / 深；切换后预览、另存、复制、详情均命中当前资产。
- [x] 提交分组组件与相应历史兼容验证。

### 任务 6：图库元数据、引用保护与清理事务

**文件：** 新增 `src/GeneratedImageStore.Management.cs`、`src/ImageAssetReferenceResolver.cs`；扩展 UsageRegistry、Store、ConversationSession / WorkspaceManager 的引用登记；新增 `tests/ImageGalleryManagementTests.cs`。

**接口：** Store 的 `SetFavorite(ids, value)`、`MoveToTrash(ids)`、`Restore(ids)`；管理服务的 `InspectReferencesAsync(ids, token)`、`PurgeAsync(ids, token): Task<ImagePurgeResult>`。永久删除统一从管理服务进入，不开放绕过引用检查的公共删文件 API。

- [x] 写 `OldAssetsDefaultUnfavoritedAndVisible`、`TrashKeepsChatReadable`、`ArchivedAndUnsavedMessagesProtectAssets`、`NewReferenceCannotRacePurge`、`UnreadableHistoryBlocksPurge`、`SharedReferenceSurvivesOtherResultPurge`、`DeleteRollbackAndRestartNeverResurrectPurgedFiles`。
- [x] 运行 `Build.ps1 -Target ImageGalleryManagementTests`，记录红；故障覆盖索引提交前后和隔离目录清理阶段。
- [x] 实现索引元数据更新和默认 List 排除回收站；Get / Read 保持可用。引用 resolver 扫描所有有效会话 / 归档历史，并结合内存消息、待保存历史租约、任务参考图及其他资产引用。
- [x] 实现同一协调器下的引用创建和排他删除；新增引用先租约、移除引用在保存确认后释放。后台扫描期间不持有 UI 锁；按清理日志和索引提交情况回滚 / 完成文件隔离事务，Recover 跳过已清理项。
- [x] 重跑图库管理、后端、History 与集成测试；批量清空只删除已确认无引用项，失败返回逐项原因；提交。

### 任务 7：图库界面与批量原图导出

**文件：** 新增 `src/ImageGalleryQuery.cs`、`src/ImageGalleryExport.cs`；修改两端 ImageWorkspacePage / ImageGalleryPanel 及详情操作；新增 `tests/ImageGalleryExportTests.cs` 和图库 UI 检查。

**接口：** `Query(ImageGalleryQuery): ImageGalleryPage`，参数包含关键词、收藏 / 回收站、服务 / 模型、来源、时间范围、排序和 Offset / Limit；`ExportAsync(ids, destination, progress, token): Task<ImageExportResult>`。

- [x] 写 `FiltersAndOrderingCompose`、`SelectionUsesStableIds`、`SelectAllMeansLoadedItems`、`ExportPreservesOriginalBytes`、`CollisionNeverOverwrites`、`CancelKeepsFinishedExports`、`TrashRestoreAndProtectedPurgeAreVisible`。
- [x] 运行新增导出 / 图库 UI 检查，确认新交互红。
- [x] 实现收藏 / 多选浮层、批量操作栏和筛选；复用两种排列和详情。仅解码当前加载缩略图，查询 / 文件读取在后台执行；筛选改变时清空选择并显示结果数。
- [x] 实现目录选择后的顺序导出、原子目标文件提交、冲突安全命名和逐项报告；确认弹层复用现有样式，引用项显示保留原因。
- [x] 运行导出和两端浅深 / 窄窗口 / 键盘 UI 验证，确认用户另存文件不参与图库删除；提交。

### 任务 8：任务中心、偏好与完成通知

**文件：** 新增两端 `ImageTaskCenterPage.cs`；修改 ImageWorkspacePage、`src/Settings.cs`、通用视觉设置页和 Avalonia SettingsWindow；扩展两端图片 UI 测试。

**接口：** 新偏好 `ImageGenerationConcurrency`（1–4，默认 1）和 `ImageGenerationMotionEnabled`（默认 true）；中心订阅 Manager快照，不持有网络任务 CTS。完成通知复用既有应用通知入口。

- [x] 写 `ThreeEntriesAppearInSameCenter`、`CancelOnlySelectedJob`、`RetryCreatesNewIdAndOneRequest`、`DeliveryRepairDoesNotRegenerate`、`ReopenViewRebindsExistingJob`、`RestartShowsInterruptedWithoutNetwork`、`SettingsDefaultsAndRoundTrip`。
- [x] 运行 Windows / Avalonia UI 检查确认红。
- [x] 添加“任务”第三标签、未完成数量、全部 / 排队中 / 进行中 / 已完成 / 失败及取消过滤、来源、阶段、耗时、结果和操作；批量取消仅作用于明确选中的未完成项。
- [x] 实现手动重试的请求预览及当前服务确认、完成通知跳转、并发设置与动效偏好；关闭视图不结束任务，真正退出按 5 秒预算停止和写盘。
- [x] 重跑两端 UI 和队列 / 恢复测试，验证快照事件不重复注册、隐藏页面无重绘、配置删除与跨窗口取消一致；提交。

### 任务 9：整体验收、文档与版本

**文件：** 新增 `docs/image-workflow-dev3-validation.md`；更新 README、CHANGELOG、使用说明、Build.ps1、PortableTests、两端测试入口和 `.github/workflows/check.yml`；验收结束才修改 `src/ReleaseInfo.cs`。

- [x] 运行本版新增的任务存储 / 管理、动效、多图、图库管理 / 导出、集成和两端 UI 测试；保留首次失败与修复后证据。
- [x] 回归既有图片后端 / 六协议 / 布局、聊天附件预览、History、Queue、Agent 协议、权限、问题交互、主题与窗口生命周期，以及共享核心完整套件。
- [x] 真实服务按明确的小规模场景检查：页面与对话同时排队、取消、同回复多图切换、图库导出 / 回收 / 恢复、重启不重发。模拟与真实调用分开记账；不重用聊天中明文 Key 编写测试或文档。
- [ ] Windows 两端浅深／窗口／动效检查已完成；macOS 真机预览／保存与系统减少动画探测未执行，平台 CI 待推送后验证。原验收项：人工或平台 CI 检查浅深主题、窗口大小、减少动画、后台资源占用、macOS 原生预览 / 保存；未执行项写明，不以 Windows Avalonia 代替真机。版本更新为 `0.10.2-dev.3`，数字 AssemblyVersion 仍为 `0.10.2.0`，构建身份绑定最终提交。
- [x] 更新 README 完成项与本版验收记录，保留其余三项未勾选 TODO；完成代码审查、干净源码构建及打包。推送、标签和 Release 按用户届时授权执行，不覆盖 dev.2 标签。

## 计划自检

- 设计的三个入口、动效、图库、任务中心、多图和减少动画均有对应任务与验收。
- 数据类型、阶段和 DeliveryState 分开；生成成功但聊天失败不会再次计费。任务日志、资产与历史都保留原 ID 归属。
- 引用保护覆盖未打开 / 归档 / 未保存历史；永久删除的并发及崩溃路径有独立测试。
- 后续三项仅保留在 README / 路线图，不混入本版实现或验收计数。
- 设计及任务 1–8 已按分项评审交付；最终本地回归、真实服务限制、版本和打包状态在任务 9 与验收记录中更新。
