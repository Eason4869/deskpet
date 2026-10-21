# 技能仓库发现与安装实施记录

基线：`v0.10.1-dev.2`，提交 `594363a`。用户已批准聊天中的完整方案并要求直接实施。

目标：在“插件 → 技能”完成选定 GitHub 仓库内的搜索、正文预览、安装，以及下一次工作请求的真实 load_skill。附带已批准的 ZIP 和本机目录入口。

架构：独立的仓库配置及缓存、GitHub 读取客户端、ZIP 读取器、发现界面；安装复用 ManagedSkillLibrary 的校验与事务登记，来源与技能一并提交。公开仓库无需 Git 或账户。默认分支或指定分支解析为固定提交，预览与下载用同一提交并校验 Git blob SHA。不会运行技能脚本。

范围：名称/描述/来源搜索，仓库勾选、添加、分支修改、移除、刷新，缓存离线预览，取消、限流及故障提示，重名拒绝覆盖。自动更新、跨工具同步、私有仓库留到后续。移除仓库不移除已安装技能。安装默认启用，下一次请求生效。

界面：沿用现有字体和 ThemeManager 的 Surface、CardSurface、FieldSurface、SelectedSurface、ActionSurface；左对齐，顶部搜索与操作、可折叠仓库管理、列表及内联预览；窄窗口纵向堆叠。不会在打开页面时发起请求。

## 实施任务

- [x] 1. 测试驱动核心：仓库与分支校验、持久化、根目录和多层技能发现、固定提交、元数据解析、边界校验、故障保留缓存。
  - 新增 src/AgentKernel/SkillRepositoryCatalog.cs、SkillRepositoryClient.cs、SkillArchive.cs；tests/SkillRepositoryTests.cs。
  - 扩展 AgentSkills 共享元数据解析，ManagedSkillLibrary.Import(source, origin) 和 ManagedSkillInfo.Origin。
  - Build.ps1 增加测试目标；SDK Core 已通过 AgentKernel 通配包含核心文件。
- [x] 2. 测试驱动界面：SkillDiscoveryPanel 和 PluginsPage 添加菜单、仓库管理、搜索、只读预览、安装状态、取消与 ZIP 选择。
  - tests/SkillDiscoveryUiTests.cs 验证真实 WPF 点击、持久化、主题和窄屏，不连接外部 AI/MCP，不执行脚本。
- [x] 3. 回归与交付：仓库测试、现有 ManagedSkillLibraryTests、PluginsUiTests、AgentInstructionTests；构建应用，公开真实仓库完整流程；保存截图及验证记录，更新使用文档与 CHANGELOG。

## 验收重点

- 根目录 SKILL.md、skills/<name>、任意分类嵌套目录都能发现；坏元数据与零结果分开呈现。
- 刷新失败/取消保留最后成功缓存；不完整树不能伪装成功；缓存标明刷新时间。
- 同名技能、软链接、路径穿越、Windows 文件名碰撞、资源超量、失效提交、正文变化均不产生半安装登记。
- 安装及来源一起登记，重启可见；仓库删掉后已安装技能仍可 load_skill。
- 360/460/1050 DIP、浅/深色、添加菜单及 MCP 切换保持正常。

## 进度

- 基线已确认与标签剥离提交一致；独立分支 codex/skill-repository-discovery。
- 基线 PluginsUiTests：41 项通过。
- 实施方法：本会话连续执行，完成后独立代码审阅，不合并或发布。
- 核心测试 42 项、界面测试 35 项；真实 anthropics/skills 的 brand-guidelines 经实际界面安装并成功 load_skill。GitHub 提交 dbd4588f9e1033efb41dad4bef2f7947c8993d44。
- 独立审阅的两个 Important（分支大小写导致配置无法读取、空缓存元素导致界面异常）均用失败测试复现并修复。
- 用户追加范围：快速对话仅首次消息成功入队后登记；按会话保留运行期间文字草稿；新建入口复用相同项目/模式/伙伴/分区的未发送草稿。普通发送成功后清空，提交失败保留；未扩展为退出应用后的磁盘草稿。
- 最终九组本地检查共 289 项通过，真实仓库流程 6 项通过；对话追加范围独立审阅通过。Windows App 与 .NET 10 共享核心构建成功。最终验证范围与限制见 ../skill-discovery-validation.md。
- 用户后续 UI 约束：主页面主体保持原版，仅添加下拉；三种添加方式在内置遮罩弹层完成，不创建独立 Window。新增 SkillAddOverlay 与 SkillFolderImportPanel；外框 18px 圆角、最大 860x740、浅深主题随系统组件，正文独立滚动、关闭/Esc/遮罩返回原页。相应界面测试扩展为 43 项。
- 浮窗最终补齐慢复制关闭锁与预览后正文变化检查，核心 43 项、界面 44 项；九组本地检查合计 299 项，真实内置浮窗仓库流程 10 项。独立审阅的浮窗两项 Important 均经失败回归验证修复。
