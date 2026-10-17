# Agent 增强交付记录（2026-10-17）

当前复核基线为 `963690b`，工作分支为 `codex/agent-workflow-refinement`。改动仍在本地工作区，未提交、未推送、未发布、未替换现有安装。实现背景见 [对话与 Agent](conversation-agent.md)，内核能力来源见 [Codex + OpenCode 能力矩阵](superpowers/specs/2026-10-17-codex-opencode-kernel-capability-matrix.md)。

## 功能与界面

- 项目目录、项目工作区、多个项目与多个会话使用稳定 ID 持久化，默认项目名为“默认项目”；切换会话时保留后台 workspace、独立历史与独立队列。侧栏项目支持折叠、会话数量、活动状态圆点、项目/会话图标和独立滚动区；项目名称可在项目设置中编辑；项目和对话标题支持完整换行显示，右键菜单提供重命名、归档和永久删除。展开/收起使用开合文件夹图标。对话标题首回合后由当前模型生成，手动重命名后不再自动覆盖。项目/会话切换复用同一个聊天窗口实例和输入草稿隔离，保持只有一个可见聊天窗口。
- 文件查找、分页、超时正则搜索、批量读取；跨文件补丁、外部改动冲突检查、备份预览与恢复；文件差异提供真实 `+N/-N`、行级增删与截断/未知状态。
- 用户配置的项目命令 ID、退出码与有界输出；停止、超时、父命令退出会清理子进程。计划模式禁止命令和项目修改，并从协议工具列表移除非只读工具。
- 实际计划步骤、工具状态、文件变更、命令结果和上下文压缩检查点持久化；恢复由明确操作发起，不自动重放外部副作用。
- `.docx/.xlsx` 使用内置 OOXML 读写；`.doc/.xls` 依赖本机 Word/Excel/WPS COM，打开前禁用宏/事件并提供取消和清理。
- 链接在输入预览、用户消息和 AI 输出中使用统一的可点击圆角样式；文件卡片支持打开、打开方式、资源管理器、复制路径/内容和另存为。输入框提供 Markdown 实时预览。快速对话强制纯聊天；完整对话默认工作模式，可切回对话模式。输入框内预测默认关闭，第一次 Enter 确认 ghost，第二次 Enter 发送。
- GitHub/网页读取与 GitHub Issue/PR 写入属于工具层，使用显式仓库范围和加密连接配置；GUI 不承载连接器业务规则。

## Codex + OpenCode 风格内核

- `AgentSessionModel` / `AgentSessionStore`：稳定幂等输入、pending/delivered inbox、Step 事件与重启持久化；`ConversationSession`、`ConversationQueue` 和独立 `ConversationWorkspace` 已接入。
- `AgentToolRuntime`：统一工具注册、稳定 CallId、输出边界、取消、权限判定与审计；原生模型工具调用把协议 CallId 传入执行和审计。
- `AgentPermissionRuntime` / `AgentAuditLog` / `ExecutionClaimStore`：`allow/deny/ask`、资源匹配、审批接口、任务/工具/权限审计与 write-ahead 执行声明；恢复不重放已执行写入。
- `AgentProfileRegistry`：`build`、`plan`、`explore`、`general` Profile；`AgentModelCapabilities` 描述上下文窗口、工具、流式、推理、图片和音频能力，未支持工具不会进入协议 Schema。
- `AgentSubagentManager` / `AgentSubagentScheduler`：子 Agent 排队、并发上限、启动/中断/完成/失败，队列提升保留稳定子 Agent ID。
- MCP 通过统一 Tool Registry 接入：HTTP JSON-RPC、stdio JSON-RPC 初始化/`tools/list`/`tools/call`、Schema 属性映射、工具名校验、重复工具防覆盖、计划模式过滤和进程清理。
- `AgentContextBudget`：上下文压缩保留任务目标、最近指令、工具身份和思考签名，并写入可恢复会话事件。

## 最终验证（2026-10-17 最终源码）

| Target | Result |
| --- | --- |
| ProjectCatalogTests / ConversationWorkspaceTests / ConversationWorkspaceManagerTests | 5 / 4 / 3 |
| ProjectSidebarSmoke / ConversationSingleWindowSmoke | 14 / 7 |
| AgentDiffTests / AgentChangePreviewTests | 6 / 3 |
| AgentChangesTests / AgentDocumentTests | 31 / 24 |
| AgentWorkbenchSmoke / WorkSettingsTests | 37 / 35 |
| PredictionTests --ui / QueueTests / SteeringSmoke | 48 / 45 / 20 |
| ConversationUiSmoke / ComposerSmoke / AgentContextTests | 82 / 87 / 28 |
| MarkdownTests | 18 |
| ConversationRepeatSmoke / ConversationTitleTests | 3 / 2 |
| AgentSessionModelTests / AgentKernelTests / AgentKernelIntegrationTests | 5 / 8 / 4 |
| AgentTaskBindingTests / AgentContextRecoveryTests / AgentProfileTests | 5 / 3 / 4 |
| AgentProviderKernelTests / AgentSubagentToolTests / AgentIntegrationFinalTests | 7 / 2 / 5 |
| AgentKernelCompletenessTests | 13 |
| AgentWorkspaceTests / AgentProtocolTests | 87 assertions / 23 |
| .NET 10 共享回归 | **325 passed, 0 failed**（`work/portable-report-v062.json`） |
| PackagingSmoke | 全部通过：干净源码构建、便携包、真实更新助手整包应用、设置/历史/皮肤保留、源码包排除 `research/` 本地参考仓库 |

应用已用最终源码重建。本地测试预览：`work/kernel-preview-12/桌面伙伴.exe`。`PackagingSmoke` 使用编译响应文件解决源码文件数增加后的 Windows 命令行长度限制；`Build.ps1` 保持 UTF-8 BOM。

日志和报告位于 `work/`。付费云端模型、真实 GitHub 仓库写入、外部 MCP 服务和 macOS 原生应用未在本轮实测；MCP HTTP/stdio 协议由确定性夹具覆盖。`.doc/.xls` 转换依赖本机 Word/Excel/WPS，未安装时提示改用 `.docx/.xlsx`。`TextProviderEditor.cs:215` CS4014、`LiveTests.cs:73` CS0067 和 Windows COM CA1416 为既有或平台分析器警告。

## 代码审查结论

最终整分支审查覆盖正确性、可读性、架构、安全和性能。已修复审查发现的 MCP Schema 嵌套错误、子 Agent 排队提升改变 ID、MCP 重名工具覆盖、无效 JSON 抛出、Provider ID 误判为不支持工具、MCP 进程生命周期、同窗口项目/会话切换、完成后二次发送、左侧项目标签折叠/计数/滚动、黑色提示闪烁、完整标题显示、右键重命名/归档/删除、默认项目、开合文件夹图标、项目名称编辑、AI/手动会话标题、新建项目弹窗、Markdown 链接样式、文件卡片操作和最大化底部裁切，以及 PackagingSmoke 的 `research/` 长路径污染。未发现新的 Critical/Important 阻断项。

保留基线已有的 `TextProviderEditor.cs:215` CS4014 编译警告，未将无关整理混入本次工作。
