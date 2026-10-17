# 原生 Agent 实现与边界

桌面伙伴使用已有 C# HTTP 客户端直接调用用户配置的模型 API。工具循环在应用进程内运行，不启动 Python、外部 CLI 或本地桥接服务器。

## 开源参考

2026-10-16 实际阅读了 [Celebez/OpenManus-Lite](https://github.com/Celebez/OpenManus-Lite) 的以下源码与 MIT 许可证，当时 `master` 提交为 `1aed3c29ec12be7f68dc5471d48eacd84c891565`：

- [BaseAgent.run](https://github.com/Celebez/OpenManus-Lite/blob/1aed3c29ec12be7f68dc5471d48eacd84c891565/app/agent/base.py)：用步数限制约束循环，维护本次任务的消息历史。
- [ToolCallAgent.think / act](https://github.com/Celebez/OpenManus-Lite/blob/1aed3c29ec12be7f68dc5471d48eacd84c891565/app/agent/toolcall.py)：请求模型选择工具，把工具结果按调用 ID 回填，再请求模型。
- [MIT 许可证](https://github.com/Celebez/OpenManus-Lite/blob/1aed3c29ec12be7f68dc5471d48eacd84c891565/LICENSE)。

本项目参考上述循环结构，`src/NativeAgentClient.cs` 与 `src/AgentWorkspace.cs` 是独立编写的 C# 实现，没有复制或翻译该项目源码，也不是其完整 fork。未引入该项目的 Python 依赖、Shell 执行器、浏览器或多 Agent 系统。

## 请求与流式展示

OpenAI 兼容接口使用 `chat/completions` 的 `tools`、`tool_calls` 和 `role=tool`；Anthropic 使用 `messages` 的 `tools`、`tool_use` 和 `tool_result`。工具执行后保留本次运行的完整协议消息，包括调用 ID、OpenAI 推理字段以及 Anthropic 思考签名。会话历史只保存可展示的回答、思考和工具摘要。

SSE 正文和推理逐块回调；工具参数在内存中按调用索引拼接。只有收到协议结束事件且调用参数完整、结束原因允许工具调用时，才会执行该轮工具。连接截断、取消、格式错误或 Token 截断均不执行该轮工具。流式接口返回完整 JSON 时直接解析同一次响应，不额外重发请求。

工具轮的正文是暂定内容。首个 `running` 工具事件到来前，该轮正文已经全部回调；会话层把这些内容移入思考区并清空回答区。最后没有工具调用的一轮成为正式回答，继续实时显示。工具事件按 `running`、`complete` 或 `error` 更新，摘要仅含操作和截断后的路径预览，完整访问模式会显示绝对路径，不展示写入内容或完整工具输出。

## 工具与计划模式

原生工具由 AgentToolCatalog 统一定义，两种协议及只读策略使用同一目录。除原有文件工具外，包含 find_files、read_files、apply_patch、list_changes、restore_file、update_plan、list_commands、run_command、read_output。相对路径以工作区为基准；限制工作区模式仅能访问工作区及其子目录，完整访问模式允许操作系统和工具权限范围内的本机绝对路径。系统指令提供实际工作区、卷根、桌面、文档和用户目录，避免模型猜测路径。

工具可以创建文件夹、读写文字文件，以及通过资源管理器打开目录；不提供任意 Shell、删除或任意网络工具；仅执行用户配置的项目命令 ID。写入和修改的具体校验、可配置备份、敏感路径与链接防护集中在 `AgentWorkspace`。

计划模式由工作区 `ReadOnly` 强制执行：模型只获得列出、读取和搜索工具；即使服务返回写入调用，执行层仍拒绝。系统提示也要求只分析并输出计划，关闭计划模式后才能修改文件。关闭 Agent 的普通聊天不传工具定义，也不会执行模型擅自返回的工具调用。

## 资源限制与费用

每次任务默认最多请求模型 12 轮，可配置为 1–48 轮；工具调用预算为轮数的四倍、最多 192 次，每轮最多接受 16 个调用。最后一轮若仍请求工具则停止，不再执行。补丁参数最多 131,072 字符，其他参数最多 32,768 字符，每轮正文、推理各最多 50,000 字符，总生成内容最多 200,000 字符，工具结果达到 80,000 字符时压缩较早结果，压缩后仍超过 120,000 字符则停止。单次模型响应最多 2 MB，请求最多 8 MB，累计请求最多 64 MB；任务总时限默认 10 分钟、可配置为 1–60 分钟，单次网络请求仍使用现有客户端超时。

服务返回用量时累计输入与输出 Token，上限 65,536；服务不提供用量时依靠轮数、文字及字节预算约束，不能据此保证实际 Token 数。Anthropic 每轮设置 `max_tokens=4096`；OpenAI 兼容接口保持现有请求兼容性，不强加 `max_tokens`。停止任务会阻止后续工具与请求，已经完成的修改需要根据备份或项目版本管理恢复。

一次任务可能产生多次付费模型请求，工具结果及所选文件内容会发送到当前配置的 API 服务。循环上限和 Token 预算用于限制消耗，不代表固定费用或免费额度。

## 平台与验证

协议传输使用共享 C# 代码，面向 .NET Framework 4.8 与 .NET 10。当前文件 Agent 仅支持 Windows，工作区实现依赖 Windows 文件句柄与链接防护；其他系统会拒绝创建文件工作区，可以关闭文件 Agent 使用普通聊天。服务必须支持所选协议的原生工具调用，只有纯文字补全能力的接口无法完成工具任务。

`tests/AgentProtocolTests.cs` 使用本机 TCP 服务覆盖两种协议的真实多轮写读、SSE 参数分片、最终正文实时显示、思考回放、JSON 兼容响应、禁用工具、取消、截断、循环限制、工具失败、用量预算和超时。测试不使用真实账号，也不能替代用户服务端的模型权限、计费与网络验证。

## 2026-10-17 能力参考与实现

本次查阅 [OpenCode 固定提交 bc5ebf0](https://github.com/anomalyco/opencode/tree/bc5ebf00a4c169ece10f276e6ce02eaf18c326e6) 中的工具目录、read/glob/edit/shell、permission、snapshot、file-mutation、tool-output、session runner 与 compaction，以及 MIT 许可证。参考工具边界、分页读取、命令生命周期和有界上下文原则，以原生 C# 独立实现，未嵌入外部 CLI、服务或新增运行时 EXE。

交互参考 [Codex App](https://openai.com/index/introducing-the-codex-app/) 的任务与执行结果呈现，以及 [Mimo Code Desktop](https://github.com/mimo-code-desktop/mimo-code-desktop/blob/main/README.zh.md) 的桌面工作流。后者采用 PolyForm Noncommercial，本项目仅参考交互需求，没有复制其代码或界面资源。保留 DeskPet 默认窗口的 WPF UI；最大化时按用户补充要求显示项目/任务导航和任务详情侧栏，复用同一会话与输入框。新增可折叠任务面板、命令设置及文件前后预览。

WindowsCommandRunner 使用挂起进程创建、加入 kill-on-close Job Object 后恢复线程，失败时终止挂起进程；stdout/stderr 独立排空并限制存储，退出/取消/超时清理后代。命令程序和参数来自用户设置，模型只能传 command_id；这是权限控制，不是隔离项目脚本副作用的系统沙箱。

AgentChanges 记录读取哈希、写入前后哈希和原备份。跨文件补丁先预校验全部条目，每个文件原子替换；不承诺整个补丁事务回滚。中途失败明确报告部分完成，已修改文件与备份仍可查看。修改前后检查外部变化，恢复需匹配当前哈希及备份哈希；这些检查不构成对恶意并发写入者的操作系统级 CAS 保证。

AgentTaskStore 按伙伴/项目键加密原子保存，检查点最多 8 MiB。损坏记录保留并阻止覆盖，用户明确清除后重建。启动仅恢复状态，继续任务由用户触发，执行历史不自动重放。计划模式仅允许文件读取与任务元数据记录，命令及文件修改同时在 schema 与执行层禁用。

检查点保存失败时按伙伴/项目保留最新待保存快照，刷新不会用旧磁盘状态覆盖，警告仅在成功保存或明确清除后消失。单任务变更上限 256，执行层在修改前拒绝越限，保存和加载使用一致的验证规则。
