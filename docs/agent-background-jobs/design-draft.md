# Agent 后台任务等效设计合同（待确认）

状态：提案，尚未批准或实现。目标版本 `0.10.5-dev.2`。

原始计划和测试清单引用的 `2026-10-23-agent-background-jobs-design.md` 未提供；已搜索 QQ 下载目录、工作区文件和现有 Git 历史，没有找到。本文件明确区分已有要求与为补齐协议提出的选择，不冒充原设计。

依据：[统一计划 S8–S17](../localization/planning/2026-10-23-localization-and-agent-background-jobs-plan.md)、[后台实施计划](../localization/planning/2026-10-23-agent-background-jobs.md)、[A01–A32/F01–F06 测试清单](../localization/planning/2026-10-23-agent-background-jobs-test-checklist.md)。三份归档资料保留原样；当前用户指令授权继续开发及目标版本，覆盖其中“本轮仅规划”的历史限制。

## 1. 已确定的产品范围

用户在项目工作会话中让 Agent 启动构建、测试或本地预览服务，启动工具及时返回；下一轮仍可读取日志、查询状态和停止。关闭任务面板、切换会话或短命 AgentWorkspace Dispose 不停止已经交接的任务。退出应用必须清理全部任务，不提供退出后常驻。

顺序为共享核心/Windows 执行器 → WPF 完整验收 → Windows Avalonia 最小持久项目绑定及完整验收 → 总验收。macOS 仅验证共享业务和 UI 能力门禁，不注册或执行三个任务工具，也不构造 Windows 工厂。保留 0.10.4 修复与 0.10.5-dev.1 语言资源合同。

应用级唯一管理器持有任务；短命工具工作区只获得可信 Owner。无新运行时依赖、守护进程、Docker、管理员权限或自动放宽网络。完成事件不调用模型、不隐式把日志写入对话。

## 2. 已有硬限制与权限

| 项目 | 合同 |
| --- | --- |
| 后台时限 | 默认 1800 秒；请求整数 1–28800，且不超过项目有效上限；旧同步 exec_command 仍默认 120、范围 1–600 |
| 容量 | 全应用最多 4、每项目最多 2 个活动任务；不排队；CleanupFailed 保留名额 |
| 日志 | 每任务最多 2×4 MiB 段（含封装），全应用 128 MiB；活动任务预留 8 MiB；待写队列 ≤256 KiB，单记录正文 ≤8 KiB UTF-8 |
| 历史 | 终态最多 200 项且不超过 FinishedUtc 起 7 天；活动记录不得淘汰 |
| 响应 | 扣除审批及沙箱准备后，启动返回 ≤2 秒；停止 ≤5 秒、正常退出 ≤10 秒、EOF 收尾 ≤2 秒 |
| UI | 长任务时交互 ≤500 ms；刷新 ≤4 Hz；浅深主题、390 DIP 和宽窗 |

只允许持久化项目工作会话，Owner 为 projectId + conversationId + 固定主助手 agentId=primary。普通聊天、Unassigned、Quick、Draft 和只读子 worker 均不能启动。

Plan/只读或关闭终端时拒绝启动，包括伪造 JSON 直调；原 Owner 进入 Plan 后仍可 get。get 为 ReadOnly，新 call_id 查询实时状态。停止不执行 shell，关闭终端后仍保留可信 UI 停止路径。

start 采用现有 shell 权限与审批：Auto 沙箱 shell 仍人工审批、Full 沿用既有规则，不自行增加权限。冻结实际命令、规范化 cwd、有效时限、Owner、payload hash、权限版本和目录身份；审批后、创建前再次检查。一次 start 仅审批一次，容量在审批后再次检查。

## 3. 三个工具的精确参数（本提案补齐）

三个 schema 都是 object、additionalProperties=false；拒绝未知参数、JSON 类型错误及数值截断，不从外部接收 Owner、PID、路径型日志句柄、环境变量覆盖、权限或沙箱开关。

| 工具 | 必填 | 可选 | 类型与边界 |
| --- | --- | --- | --- |
| start_job | command | title、working_directory、timeout_seconds | command 非空 UTF-8 ≤16 KiB；title 非空 UTF-8 ≤512 字节；working_directory 为字符串，默认当前工作会话目录，必须规范化并服从既有目录权限；timeout_seconds 整数 1–28800 |
| get_job | id | cursor、limit_bytes | id 为本机生成的 32 位小写十六进制；cursor 为不透明字符串，省略/空串从最早可读位置开始；limit_bytes 整数 1024–65536，默认 16384 |
| stop_job | id | 无 | id 同上；工具停止原因固定 user，不接受模型伪造原因 |

缺省时限为 min(1800, 项目上限)；显式超过项目上限直接拒绝。title 缺省使用非空命令首行的安全截断展示，不改变实际命令。目录身份必须与批准时一致，不能跟随目录替换或符号链接逃逸。

## 4. 返回与错误（本提案补齐）

所有响应为完整 JSON，不使用可见中文前缀表示状态，不允许 journal 的 24000 字符截断破坏返回。get 的 limit 只统计 records[].text 的 UTF-8 正文字节；JSON 转义和元数据不算入正文页限。

start/stop 成功：`{"ok":true,"job":<snapshot>,"replayed":false}`。start 对同键同 payload 返回原任务，replayed=true；stop 对已终态任务返回原终态，不伪造新退出。

get 成功：`{"ok":true,"job":<snapshot>,"records":[...],"next_cursor":"...","has_more":false,"gap":null}`。

snapshot 固定字段：id、state、project_id、conversation_id、agent_id、title、command、working_directory、timeout_seconds、created_utc、started_utc、finished_utc、root_exited、root_exit_code、stop_reason、cleanup_confirmed、may_have_executed、log_warning、timings_ms。未发生的时间和真实退出码为 null，UTC 使用 ISO 8601；不暴露原生句柄，不把 PID 当工具身份。timings_ms 记录 approval、sandbox_prepare、process_prepare、resume、handoff、stop、eof_drain，尚未发生为 null。

record 字段：sequence、stream（stdout/stderr）、timestamp_utc、text、fragment_offset。片段偏移按原记录正文的 UTF-8 字节计，绝不从多字节字符中间截断。next_cursor 绑定 job、随机日志代次、sequence 和片段位置，并验证完整性；不能接受裸偏移或路径。

gap 为 null 或 `{reason,dropped_bytes,resume_cursor}`；reason 为 rotated、buffer_overflow 或 incomplete_tail。缺口明确可见，过期合法游标可从最早剩余处继续，不静默重复/丢字。

失败：`{"ok":false,"error":{"code":"...","message":"...","retryable":false}}`。message 来自 TextResources；code 为稳定协议。跨 Owner 与随机未知 id 均只返回相同 job_not_found，不泄露存在性或来源。

代码表：invalid_arguments、unsupported_platform、workspace_required、permission_denied、policy_changed、directory_changed、job_not_found、invalid_cursor、idempotency_conflict、execution_reconciliation_required、capacity_exceeded、storage_unavailable、recovery_in_progress、application_exiting、start_failed、cleanup_failed、log_storage_failed。

retryable 仅表示以新的明确用户意图/工具调用稍后重试查询或尚未产生副作用的容量/恢复拒绝；任何可能已经执行的 start 均不得自动重跑。原始异常/Win32 码保留在本机诊断，不向模型暴露密钥、凭据或宿主任意路径。

## 5. 幂等与启动交接

已有要求：十次并发同调用只产生一次副作用；参数改变拒绝；丢响应、恢复和记录清理后均不自动重跑。短期 ExecutionClaimStore 不能充当永久保证。

本提案幂等键：SHA256(稳定规范化序列 `["agent-job-v1", projectId, conversationId, "primary", persistedTaskId, callId]`)。TaskId 与 callId 来自宿主持久执行记录，不由工具参数提供，不使用临时 AgentWorkspace/session/profile 身份。payload hash 覆盖完整规范化命令、实际 cwd、有效 timeout 和 title。

先查询该键，再检查新启动容量；同键同 hash 关联原 job、不同 hash 拒绝。宿主执行日志在可能副作用前登记，任务元数据保存 key/hash；旧调用记录缺失、被清理或需要协调时只返回 execution_reconciliation_required，不重新创建进程。只读 get 不进写操作结果缓存，不因新 call_id 返回旧页。

启动事务：规范化/校验 → 审批 → 在 Owner/目录变更门禁内复核策略和目录 → 预留名额及持久 Starting → 创建挂起进程并分配 Job → 检查取消 → Resume → 持久 Running → 原子交接后返回。

Starting 保存失败不得创建进程；Resume 后 Running 保存失败必须杀树，明确 may_have_executed=true、不自动重跑。交接前取消必须确认回收；交接后任务不再受请求 token/Workspace Dispose 支配。清理未确认保留 CleanupFailed 和名额。

## 6. 状态、执行与恢复

状态与现有计划完全一致：Starting、Running、Stopping、CleanupFailed、Succeeded、Failed、Stopped、TimedOut、StartFailed、Interrupted；wire state 使用其稳定 snake_case 小写。stop_reason 为 user、timeout、application_exit、owner_deleted、log_storage_failed 或 null。

根退出但 Job 内还有后代时保持 Running，root_exited=true 且保留真实 root_exit_code。整树结束且流收尾完成才形成一次终态事件。自然退出用真实退出码决定 Succeeded/Failed；用户停止和超时保留各自状态，不把强杀返回值冒充自然退出码。

Windows 使用挂起创建 → kill-on-close Job 分配 → 异步排流 → Resume，自有 Job 句柄不继承。Stop 只操作持有的 Job/创建身份，不按进程名清理。沙箱及授权目录租约保持至整树结束，AppContainer 准备失败不得改成宿主执行。复用现有 PowerShellBootstrap 和干净环境白名单。

数据目录拥有独占写租约；第二管理器/宿主不能启动或改写关联任务/绑定，普通聊天继续。恢复结束前不启动；恢复仅处理记录，不构造工厂、不附着、不杀旧 PID、不重跑。旧非终态变 Interrupted；损坏/半尾隔离且不污染其他记录。

元数据版本 1，单记录 ≤64 KiB，同目录原子替换。读写拒绝重解析点和非法 ID；磁盘 I/O 不在全局管理器锁内等待。日志 quota、轮转、索引刷新和故障用假时钟/注入写入器验证；发生不可恢复日志落盘失败要停止该任务且不拖累其他任务。

退出先建立拒绝新启动/待审批的屏障，再等自有任务清理；10 秒未确认不得假退出或丢弃窗口，保留重试。宿主被强杀后由 Job 句柄关闭回收整树，重开标 Interrupted。

## 7. 双端 UI 与项目操作

WPF 提供原会话工具卡片和项目任务面板；归档会话仍可查看停止。删除在同一管理器门禁作用域先阻止启动并确认清理，再改目录/目录模型；失败保持原目录。移动/改根在活动任务存在时拒绝。隐藏、归档、关闭面板不取消任务。

Avalonia 复用 ProjectCatalogStore、ConversationWorkspaceManager，增加最小项目/会话选择及必要管理，不搬迁普通聊天历史或整个 WPF 侧栏。各窗口绑定稳定 Session/Queue/EffectiveSettings，草稿、权限、审批、ask_user、图片、听写、导出和清空不得串会话。

日志和命令作为纯文本展示；ANSI/HTML/伪指令不能执行。预览只从日志提出最多 5 个回环 HTTP(S) 候选，去除 userinfo、query、fragment；不自动探测 HTTP 或声称服务 Ready。用户点击才打开真实浏览器，错误按实际结果显示。完成最多通知一次，不新增模型调用。

所有新增界面文案使用 agent.jobs.* JSON 资源，并登记新功能来源、独立基线及审计；协议字段、工具 schema 和日志正文不机械资源化。

## 8. 验收与本提案待确认项

原测试清单 A01–A32/F01–F06 全部保留。WPF 阶段完成后立即 Avalonia；不同运行时真进程、真实 AppContainer、独立退出和浏览器证据不能由假进程或 headless 截图代替。A31 的 100 次资源回收为 P1 单列，其余适用 P0 必须齐全。

本提案新增且需要确认的协议选择集中在第 3–5 节：参数名与长度、get 默认页、JSON 字段/错误码、稳定幂等键。它们由当前计划与代码推导，尚未获得原设计佐证。确认后冻结为 S8 合同；如提供原设计，先逐项比较，冲突以明确认可的原设计为准。

版本可改为 0.10.5-dev.2；本轮仅开发与验收，不因之前 dev.1 的合并/tag 授权而自动合并、打新 tag 或发布 dev.2。
