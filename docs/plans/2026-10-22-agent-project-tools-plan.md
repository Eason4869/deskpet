# 桌宠 Agent 项目工具实施计划

> **执行约定：** 按任务逐项推进；工具 schema、权限、取消及 UI 行为先补自动化验证，再实现并运行对应验证。

**目标：** 为桌宠 Agent 增加可在任务中询问用户、检查图片和读取本地 Git 状态的内置工具，减少猜测及常见终端调用。

**架构：** 首批工具复用现有 `AgentToolCatalog`、`AgentToolRuntime`、工具事件和会话取消机制。`ask_user` 通过聊天中的专用交互卡片暂停当前 Agent 调用并等待用户答复；`inspect_image` 使用受限工作区文件访问，需将本地图像作为视觉内容交给支持视觉的模型；Git 工具在进程内调用只读 Git 命令封装，限定仓库根目录、参数和输出。浏览器、技能发现/安装、文档/表格精细编辑和后台 Job 管理列入后续可选模块，不作为首批依赖。

**技术栈：** Windows 使用 C# 5 / .NET Framework WPF；共享核心及 Avalonia 使用 .NET 10；使用现有 Agent 工具 schema/运行时、工作区策略、聊天卡片与模型能力解析。Git 首版调用本机 Git CLI，不增加 Git 库依赖。

**依据：** 用户 2026-10-22 提出的内置工具优先级；首批为 `ask_user`、`inspect_image`、`git_status`、`git_diff`、`git_log`。用户要求统一内置入口，浏览器操作、技能管理和文档/表格精细编辑先做可选模块。

## 全局约束

- 新工具通过统一内置工具目录暴露，支持 OpenAI/Anthropic 工具 schema 和现有计划模式/模型能力过滤。
- 所有等待和本地读取均接受调用级取消；会话切换时结果归属原会话/任务；窗口关闭或任务取消时不得悬挂等待。
- `ask_user` 只用于获取任务输入，不等同于安全授权；写入、终端等操作的现有权限确认流程保持独立。
- `inspect_image` 不绕过工作区访问策略；在模型不支持图像输入时返回明确能力提示，不伪称已经完成视觉分析。
- Git 工具不得修改仓库或发起网络访问；不接受任意 Git 参数，不调用 shell；输出有上限并标明仓库与分支上下文。
- 首批不引入 Playwright、Office SDK 或技能市场依赖；后续模块可选启用。
- 自动化验证与真实桌面/仓库验证分别记录。

## 实施任务

### 任务 1：`ask_user` 对话交互工具

**文件：** `source/src/AgentToolCatalog.cs`、`source/src/AgentWorkspace.cs` 或新增工具分部、`source/src/ConversationSession.cs`、`source/src/AgentApprovalController.cs`（只复用会话归属/取消模式，不混用授权语义）、新增 `source/src/AgentQuestionController.cs`、`source/src/AgentQuestionCard.cs`、相关 Agent 与桌面测试。

- [x] 定义工具 `ask_user(question, options?)`：问题必填；选项可选且数量有界（2–6 条）；返回用户所选选项或自由文本。
- [x] 建立独立的问题等待控制器/卡片：在原助手消息附近展示问题、选项按钮和可选自由输入；用户提交后恢复同一 Agent 调用。
- [x] 通过稳定 `CallId`、`SessionId`、`TaskId` 绑定等待项；切换聊天不把问题移到新会话；关闭窗口、取消任务或归档会话时完成取消并释放等待项。
- [x] 对无桌面交互能力的入口返回明确错误；重复/过期提交只接受首次有效答案；问题和答案按现有消息/工具记录策略展示。
- [x] 测试：有/无选项、自由回答、选择后恢复、重复点击、任务取消、切换会话、窗口关闭、模型工具能力过滤，以及不会被误当成权限审批。

### 任务 2：`inspect_image` 与视觉工具结果通道

**文件：** `source/src/AgentToolCatalog.cs`、`source/src/AgentWorkspace.cs` 或新增工具分部、`source/src/ChatAttachment.cs`（只复用格式校验，不改变 4 MB 聊天附件策略）、相关 OpenAI/Anthropic Agent 请求与工具结果构建代码、图片工具测试。

- [x] 定义 `inspect_image(path)`，可选 `question`；路径通过现有工作区根目录和读取权限规则解析；拒绝越界、链接逃逸、非图片文件及超出独立读取限制的图像。
- [x] 读取 PNG/JPEG/GIF/WebP 等已支持类型并提取尺寸、媒体类型、字节数，工具结果包含这些元信息供记录展示。
- [x] 扩展工具结果模型和协议适配器，支持将本地图像作为图像内容块放回模型的后续上下文，而不将 Base64 文本直接塞进普通工具文本、审计日志或历史记录。
- [x] 根据模型能力发送视觉输入；纯文本模型或不支持的协议给出“当前模型不支持图片理解”的错误。检查图像传输/缩放策略和供应商图像限制，保留原文件不改写。
- [x] 测试：有效图片、宽高读取、损坏/伪造格式、超限、工作区越界/符号链接、取消、文本模型降级、OpenAI/Anthropic 图像工具结果序列化及日志不含 Base64。

### 任务 3：本地 Git 只读工具

**文件：** 新增 `source/src/AgentGitTools.cs`、`source/src/AgentToolCatalog.cs`、`source/src/AgentWorkspace.cs` 或新增工具分部、`source/src/AgentKernel/AgentPermission.cs`（若需明确 `git_read` 权限动作）、新增 `source/tests/AgentGitToolsTests.cs`。

- [x] 提供 `git_status(path?)`、`git_diff(path?, scope?)`、`git_log(path?, limit?)`。`path` 默认为当前 Agent 工作区；通过仓库发现确定 repo root，并拒绝目标仓库超出已授权根目录。
- [x] 通过 `ProcessStartInfo（框架版采用严格参数转义）` 直接启动 Git CLI，不经 shell。使用固定子命令和参数白名单：状态读取、受限差异（含 unstaged/staged，明确说明 untracked 不含补丁）、有界提交记录；禁用外部 diff/textconv，禁用可选锁写入；禁止任意选项、URL、push/fetch/checkout/reset 等副作用命令。
- [x] 对 Git 不存在、路径不是仓库、safe.directory/权限错误和命令超时给清晰提示；输出含当前分支/状态并截断到固定上限。
- [x] 作为只读工具进入计划模式；接入权限审计并标记 repo root 资源，不要求每次通过终端授权。
- [x] 测试：干净/有改动/暂存/未跟踪仓库、路径过滤、非仓库、Git 缺失、命令超时、超大 diff/log 截断、恶意参数不能变成命令选项、命令不改变 Git index/worktree。

### 任务 4：内置工具统一入口与回归

**文件：** `source/src/AgentToolCatalog.cs`、`source/src/AgentWorkspace.cs`、相关 Agent 面板/工具事件 UI、`source/docs/conversation-agent.md`、`source/docs/agent-integration-plan.md` 或验证文档。

- [x] 统一校验所有新工具的 schema、可用性、能力过滤、计划模式行为、取消、记录展示和审计动作分类；工具记录显示可读摘要，不暴露图片 Base64 或不必要的完整图片路径。
- [x] 更新内置工具说明：Agent 可以向用户提问、检查工作区图片并分析本地仓库状态/差异/历史；说明可用范围与当前模型视觉能力依赖。
- [x] 回归现有文件工具、MCP/GitHub 工具、权限审批、队列停止和切换伙伴功能。
- [x] 分别记录自动化覆盖与桌面手工验收；已用真实本地 Git 夹具验证，视觉协议使用模拟 HTTP，未声称真实视觉模型验收。

## 后续可选模块（不属于首批）

- 后台任务：`start_job`、`get_job`、`stop_job`，配合任务 ID、日志限额、生命周期和应用关闭策略独立设计。
- Web：`web_search` 与 `browser_snapshot/click/fill/screenshot`；浏览器模块独立启用，可选 Playwright 依赖。
- 技能管理：`search_skills`、`preview_skill`、`request_skill_install`；复用现有技能安装确认弹层并确保安装前明确展示来源/权限。
- 文档与表格精细编辑：`edit_document`、`edit_spreadsheet`；独立设计结构化修改、范围校验和撤销/备份策略。

## Review Focus

- 用户切换会话或关闭窗口时，`ask_user` 必须取消或继续附着原会话，不能把答案交给另一个任务（任务 1 生命周期测试）。
- `inspect_image` 必须把图像作为协议图像内容传输并限制体积，不能把 Base64 或本地图片内容写入普通工具文本/审计日志（任务 2 序列化和日志测试）。
- Git 路径、分支名和筛选参数不可注入任意 Git 选项或逃出授权仓库（任务 3 参数/路径测试）。
- 只读 Git 工具不得通过 external diff、textconv、锁更新或其他隐式行为修改仓库（任务 3 工作树/索引不变测试）。
- 视觉模型能力缺失、Git CLI 不可用或不是仓库时，Agent 应收到可操作的错误并继续任务，不得声称已检查（任务 2/3 失败路径测试）。
