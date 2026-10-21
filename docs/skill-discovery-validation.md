# 仓库技能与对话草稿验证

日期：2026-10-22。Windows 本地验证，基于 v0.10.1-dev.2（594363a），独立分支 codex/skill-repository-discovery。

## 已执行检查

| 检查 | 结果 |
| --- | --- |
| SkillRepositoryTests | 42 项通过：来源、分支、缓存、根目录/嵌套发现、资源、SHA、BOM、ZIP、路径/链接限制、限流及取消 |
| SkillDiscoveryUiTests | 35 项通过：真实 WPF 控件搜索、预览、仓库/ZIP 安装、状态、重开、取消及浅/深色、360/460 DIP 布局 |
| ManagedSkillLibraryTests | 41 项通过，包含原有登记失败回滚、链接拒绝、损坏登记保留及加载权限检查；junction 样本须在沙箱外执行 |
| PluginsUiTests | 41 项通过：本机导入、启停、详情、移除、MCP 草稿保留与主题/窄屏 |
| AgentInstructionTests | 10 项通过：真实技能加载及权限边界 |
| DraftConversationTests | 9 项通过：快速对话首次发送登记、失败保留、A/B 和新建草稿恢复、快速/完整窗口共享草稿 |
| ConversationModeUiTests | 9 项通过：聊天/工作切换、项目选择、草稿恢复和快速会话归属 |
| ConversationOwnershipUiTests | 15 项通过：伙伴私有会话隔离、共享工作记录、快速窗口切换 |
| ComposerSmoke | 87 项通过：编辑器、发送、队列、附件等已有行为；不包含原生拖拽验收 |
| .NET 10 DeskPet.Core build | 0 错误；存在既有平台与 API 警告。依赖还原的漏洞数据查询受本机 NuGet 网络限制，未声称完成漏洞审计 |
| Windows App build | 程序与更新助手均构建成功，预览输出位于 outputs/skill-repository-preview，版本标识沿用 0.10.1-dev.2 并附构建 commit |

上述九组检查共 289 项。GitHub Actions 已增加仓库核心与发现界面测试目标，本轮没有运行远端 CI。

## 真实公开仓库流程

使用 `SkillDiscoveryUiTests.exe --live`，通过真实发现页添加 anthropics/skills，搜索 brand-guidelines，查看 SKILL.md、来源与完整提交，点击安装，然后在新 AgentWorkspace 中通过真实 load_skill 读取。共 6 项检查通过。

首次验证固定提交为 dbd4588f9e1033efb41dad4bef2f7947c8993d44；最终复验的精确提交、临时目录和状态保存在 work/discovery-live-verified.log。截图位于 work/skill-discovery-evidence。没有调用 AI 服务、MCP，也没有执行技能内脚本。

公开仓库客户端与已有 GitHubClient 一致，使用直连；本机系统代理曾使默认代理连接 TLS 握手失败，已通过只读诊断区分直连和系统代理，未修改用户网络配置。网络验收在沙箱外进行。

## 独立审阅

仓库实现发现两个 Important：分支大小写去重规则不一致会损坏可读配置；缓存中的 null 元素会导致页面异常。两项均已用失败测试复现并修复，核心测试重新通过。对话追加范围的独立只读审阅未发现可确认问题。

## 范围

首次消息成功入队即登记会话，不等待模型回答成功。未发送文字保留在当前运行中的会话状态；未增加跨应用重启的磁盘草稿。首版仓库搜索范围是已添加并勾选的公开 GitHub 仓库，不包含全 GitHub 搜索、私有仓库、自动更新或跨工具同步。此处是自动化和真实仓库检查记录，不代表用户人工验收、完整全库测试或已发布版本。
