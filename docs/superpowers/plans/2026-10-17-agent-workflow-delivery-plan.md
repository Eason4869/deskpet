# Agent 工作流与 Codex + OpenCode 内核整合计划（2026-10-17）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 GitHub `plan` 分支交付计划、本地 Agent 工作流实现、Codex CLI 与 OpenCode CLI 的能力研究整合成一个可执行的 DeskPet 内核计划；优先完成会话、工具、权限和恢复，GUI 暂时保持现状。

**Architecture:** 内核采用自研 C# Session / Tool / Permission / Recovery 四层。Codex 提供线程、回合、压缩、执行策略、审批和恢复语义；OpenCode 提供 Provider、Tool Schema、Agent 模式、Inbox/Step、插件与统计语义；DeskPet 保留 Windows 文件安全、项目/会话存储、语音和当前 UI。GUI 只消费内核事件，不承载核心业务规则。

**Tech Stack:** C#、.NET Framework 4.8 WPF、.NET 10 shared tests、DataContractJsonSerializer、HttpClient、现有 HistoryStore / AgentTaskStore / ConversationSession / AgentWorkspace / WindowsCommandRunner。

**Specs:**
- `docs/superpowers/specs/2026-10-17-codex-opencode-kernel-capability-matrix.md`
- `docs/superpowers/specs/2026-10-17-multi-project-sessions-and-change-diffs-design.md`

## 0. 来源合并结论

| 主题 | GitHub `plan` 分支 | 本地内容 | 合并结果 |
| --- | --- | --- | --- |
| Agent 工作流 | 工作区、压缩、引导、命令、检查点、文件格式 | 已有实现并追加项目/会话、差异预览、GitHub 工具 | 保留本地实现；补入内核分层和后续能力 |
| “新对话” | 作为侧栏入口 | 先实现为多窗口，用户随后要求同窗口标签 | **保留差异**：最终目标改为同一窗口内项目/会话标签；多窗口方案标记为被替代 |
| 文件变更 | 变更卡、查看差异、增删行数 | 已升级为真实行级 diff、截断/未知状态 | 保留本地较新实现；计划中统一为 `AgentDiff` 能力 |
| GitHub / 网页 | 未展开 | 已加入 `fetch_url` 与 GitHub 读写工具雏形 | 保留为内核连接器能力；GUI 配置后续再做 |
| GUI | Codex App / MiMo Desktop 对齐 | 当前 WPF 基础和宽屏侧栏 | **暂不动 GUI**；只保留数据/事件接口 |
| 发布 | 推送、PR、版本、CHANGELOG | 仍未提交/推送/发布 | 保留为最后集成阶段，不混入内核实现 |

## 1. P0：会话内核

### Task 1：统一 Session / Inbox / Step 模型

**Files:**
- Create: `src/AgentKernel/SessionModel.cs`
- Create: `src/AgentKernel/SessionStore.cs`
- Modify: `src/ConversationSession.cs`
- Test: `tests/AgentSessionModelTests.cs`

**Interfaces:**
- `SessionId`、`ProjectId`、`ConversationId`、`AgentTaskId`、`MessageId`、`CallId`
- `SessionInput { Kind, Text, Attachments, Resume, Steer, IdempotencyId }`
- `SessionEvent { Sequence, Type, Payload, CreatedUtc }`
- `SessionProjection { Messages, PendingInput, ActiveStep, RecoveryState }`

- [ ] **Step 1:** 写“同一 Session 幂等输入、跨 Session ID 冲突、pending/delivered 状态”失败测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 实现稳定 ID、Inbox、Step 和事件序列。
- [ ] **Step 4:** 将当前 `ConversationLog` 和 `ConversationQueue` 映射到 Session 投影。
- [ ] **Step 5:** 运行 `AgentSessionModelTests`、`ConversationTests`、`QueueTests`。
- [ ] **Step 6:** 本地提交。

### Task 2：上下文预算与可恢复压缩

**Files:**
- Modify: `src/AgentContextBudget.cs`
- Modify: `src/AgentTaskState.cs`
- Test: `tests/AgentContextRecoveryTests.cs`

**Interfaces:**
- `ContextBudgetResult { Compacted, Summary, RetainedIds, Unknown, Status }`
- `Checkpoint { Summary, TaskFacts, ToolCalls, ThinkingSignatures, Attachments }`

- [ ] **Step 1:** 写压缩中断、摘要失败、重启恢复和晚到结果测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 统一压缩状态、摘要版本和恢复边界。
- [ ] **Step 4:** 保留工具身份、调用/结果关联和思考签名。
- [ ] **Step 5:** 运行 `AgentContextTests`、`AgentProtocolTests`、`AgentTaskTests`。
- [ ] **Step 6:** 本地提交。

## 2. P0：工具内核

### Task 3：统一 Tool Registry / Schema / Executor

**Files:**
- Create: `src/AgentKernel/AgentTool.cs`
- Create: `src/AgentKernel/ToolRegistry.cs`
- Create: `src/AgentKernel/ToolExecutor.cs`
- Modify: `src/AgentToolCatalog.cs`
- Modify: `src/AgentWorkspace.cs`
- Test: `tests/AgentToolRuntimeTests.cs`

**Interfaces:**
- `AgentToolDefinition { Name, Description, ReadOnly, InputSchema, OutputSchema }`
- `ToolExecutionContext { SessionId, AgentId, MessageId, CallId, Progress, Permission }`
- `ToolResult { Content, Output, Metadata, Truncated, Duration }`

- [ ] **Step 1:** 写注册、替换、取消、输出截断、进度和稳定 CallId 测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 将文件、补丁、命令、GitHub、网页工具迁移到统一执行器。
- [ ] **Step 4:** 统一错误分类：取消、权限、工具、格式、超时。
- [ ] **Step 5:** 运行 `AgentToolRuntimeTests`、`AgentFileToolsTests`、`AgentChangesTests`、`AgentCommandTests`。
- [ ] **Step 6:** 本地提交。

### Task 4：文件、补丁、命令安全边界

**Files:**
- Modify: `src/AgentWorkspace.cs`
- Modify: `src/AgentChanges.cs`
- Modify: `src/WindowsCommandRunner.cs`
- Test: `tests/AgentSafetyBoundaryTests.cs`

- [ ] **Step 1:** 写路径、链接、冲突、备份、命令参数和进程清理测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 统一 preflight / write / audit 三阶段。
- [ ] **Step 4:** 保留有界文件、输出、命令超时和恢复语义。
- [ ] **Step 5:** 运行文件、补丁、命令、恢复回归。
- [ ] **Step 6:** 本地提交。

## 3. P0：权限与审批

### Task 5：Action / Resource / Effect 权限模型

**Files:**
- Create: `src/AgentKernel/AgentPermission.cs`
- Create: `src/AgentKernel/PermissionRuntime.cs`
- Test: `tests/AgentPermissionTests.cs`

**Interfaces:**
- `AgentAction`: `read`, `write`, `delete`, `patch`, `shell`, `network`, `github_read`, `github_write`, `mcp`
- `PermissionDecision`: `allow`, `deny`, `ask`
- `ApprovalRequest { Id, Action, Resource, Risk, PayloadHash }`

规则要求：

- 默认拒绝未知动作和未知资源。
- 读取、写入、命令、网络、GitHub 写入分别可配置。
- 项目命令仍只接受用户配置的命令 ID。
- 每次决策写入稳定 CallId 对应的审计记录。

- [ ] **Step 1:** 写 allow/deny/ask、资源匹配、默认拒绝和审计测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 实现规则合并、优先级和审批请求。
- [ ] **Step 4:** 将 `AgentWorkspace.ExecuteAsync` 全部工具调用接入权限层。
- [ ] **Step 5:** 运行 `AgentPermissionTests`、`AgentCommandTests`、`AgentProtocolTests`。
- [ ] **Step 6:** 本地提交。

## 4. P0：恢复与执行可靠性

### Task 6：Write-ahead 执行声明与恢复

**Files:**
- Create: `src/AgentKernel/ExecutionClaim.cs`
- Modify: `src/AgentTaskStore.cs`
- Test: `tests/AgentRecoveryTests.cs`

- [ ] **Step 1:** 写启动恢复、重复执行、失败任务、外部副作用不确定和恢复取消测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 实现执行声明、尝试次数、完成/失败/中断释放。
- [ ] **Step 4:** 恢复时先核对文件/命令状态，不自动重放写入。
- [ ] **Step 5:** 运行恢复、检查点、协议回归。
- [ ] **Step 6:** 本地提交。

### Task 7：审计、事件与可观测性

**Files:**
- Create: `src/AgentKernel/AgentAudit.cs`
- Test: `tests/AgentAuditTests.cs`

- [ ] **Step 1:** 写输入摘要、权限决策、工具结果、耗时和重试事件测试。
- [ ] **Step 2:** 验证 RED。
- [ ] **Step 3:** 接入 Session / Tool / Permission / Recovery 事件。
- [ ] **Step 4:** 保证 token、密钥、认证头和原始响应不进入日志。
- [ ] **Step 5:** 运行审计回归。
- [ ] **Step 6:** 本地提交。

## 5. P1：连接器与模型层

### Task 8：Web / GitHub / MCP 适配器

- 保留 `fetch_url`、GitHub 读写工具。
- GitHub Token 加密，仓库范围显式，写操作走审批。
- MCP 只通过统一 Tool Registry 接入。
- 每个连接器限制响应大小、超时、来源和敏感字段。

### Task 9：Provider 与 Agent Profiles

- 保留当前 OpenAI / Anthropic / 兼容 Provider。
- 增加模型能力描述：上下文窗口、工具支持、推理、图片、音频。
- 引入 `build`、`plan`、`explore/general` Agent Profile。
- 子 Agent 只能在权限规则允许时启动。

## 6. GUI 暂缓项

以下不进入 P0：

- ChatGPT App / Codex Desktop 视觉重构。
- 复杂 Code Mode JavaScript 解释器。
- 云端 Agent tree、OAuth、clustering、完整 LSP。
- 复杂成本面板、模型市场、OAuth GitHub 登录。

GUI 只需要逐步消费以下内核事件：

`session.changed`、`message.added`、`tool.started`、`tool.progress`、`permission.requested`、`approval.required`、`diff.ready`、`recovery.available`、`audit.updated`。

## 7. 统一验收

- [ ] 会话可以独立创建、排队、暂停、恢复和重启。
- [ ] 工具具有稳定 Schema、CallId、输出边界、取消和进度。
- [ ] 权限默认拒绝，敏感操作可审批，审计不泄露密钥。
- [ ] 恢复不会重复已执行写入，也不会丢失未完成状态。
- [ ] GitHub/网页能力属于工具层，不依赖 GUI。
- [ ] 现有 DeskPet UI 不被重构，只接事件。
- [ ] 每个阶段均有独立测试和全量回归。
