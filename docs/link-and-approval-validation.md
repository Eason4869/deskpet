# 链接、内嵌审批与项目事实验证

日期：2026-10-21。基准：`origin/dev` 的 `59be54e2d13d4bf79e1e402252a2952c70e2971a`。

## 行为与保留决策

- 消息中的普通链接和 Markdown 命名链接均支持右键菜单：侧边标签页、默认浏览器和复制完整地址。保留单击行为；邮箱交给系统默认应用，网页标签仅接受 HTTP/HTTPS。
- 人工审批放在所属会话的输入框上方。命令和目录直接可见，原始参数及来源编号按需展开。请求绑定原会话和调用，切换会话后仍待处理，停止后不能补授权执行。
- 项目事实保留：`NativeAgentClient` 会将用户手动维护的信息引入当前项目后续任务。它可记录构建方式和目录约定，补充单次上下文压缩；管理入口改用主窗口标签页。存储仍按项目隔离、加密并原子写入，没有增加自动生成或跨项目共享。
- 网页读取错误作为工具结果返回模型。Agent 开始任务后若失败，保留原对话与任务记录，暂停后续输入，避免重放已开始的克隆或命令。任务尚未开始时的配置错误仍保留队列输入。
- 删除 `exports/E包头像与立绘` 中的三张导出图片。

## 验证范围

Windows 本机执行 21 个相关测试目标及应用编译。包括：

| 目标 | 结果 |
| --- | --- |
| UnifiedWorkspaceTests | 90 项；真实鼠标右键、会话切换、内嵌审批位置、HTTP 网页标签、窄窗口布局 |
| MarkdownTests | 26 项；完整地址复制、邮箱限制、深色链接颜色 |
| AgentApprovalUiSmoke | 33 项；允许、拒绝、取消、跨会话隔离、关闭竞态及两种主题 |
| SummaryNavigationTests | 23 项；事实标签页、空状态、新增、编辑、删除、持久化及主题 |
| DetailedGuideTests | 60 项；审批练习不执行命令、不创建审批窗口 |
| ThemeTests | 22 项 |
| ComposerRenderingTests | 110 项 |
| QueueTests | 52 项；已开始任务不重新入队，未开始的输入保留 |
| GitHubToolsTests | 13 项；HTTP、连接、响应读取、超时和取消，保留写入失败的核对机制 |
| AgentProtocolTests | 25 项；OpenAI 与 Anthropic 协议链路 |
| ConversationSwitchRegressionTests | 36 次会话切换与悬停交互 |
| BrowserPaneTests | 14 项离线控件检查 |

其余目标：AgentApprovalTests、AgentReliabilityTests、AgentSessionTests、AgentTaskMemoryTests、ConversationTests、ConversationWorkspaceManagerTests、AgentRunRecoveryTests、AgentTaskTests、AgentWorkspaceTests。

网页端到端检查使用真实 WebView2 和本机 HTTP 页面；模型回应及网络错误使用受控夹具，不依赖真实账号或付费模型。没有将这次验证表述为实际 GitHub 克隆成功或 macOS 界面验收。

截图保存在本地忽略目录 `work/permissions`、`work/approval-evidence` 与 `work/facts-evidence`。WebView2 页面另用原生捕获保存为 `work/permissions/link-page-native.png`，避免 WPF 位图捕获无法包含原生浏览器画面的问题。

编译保留原有 `AgentTaskPanel.cs` 中的 CS4014 警告。本次按 `0.10.1-dev.2` 提交 dev 分支；后续发布授权要求在最终 CI 通过的提交上创建固定标签，校验完整附件后发布 prerelease，不替换正式版 latest。
