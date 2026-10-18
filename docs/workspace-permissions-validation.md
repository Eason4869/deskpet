# 工作区权限与界面改进：本地验证

2026-10-18。前置源码快照 `ae8f03221ad3f3574413000a98169e69ed612af9` 已按要求推送 `dev`，并创建 `v0.8.0` 标签；没有创建 GitHub Release。本文对应其后的本地 `codex/workspace-approval-sandbox` 改进，未推送、未发布，也未替换正在使用的预览。

## 行为与交互

- 完整窗口采用聊天／工作双入口，分别保留草稿、当前会话和侧栏内容。后台工作继续运行并显示状态；个人记录仍按伙伴隔离。两个入口的文字使用相同行高与基线，并与头像居中对齐。
- 快速对话固定为个人聊天，不显示入口切换或工作权限，输入提示为“想聊点什么？”。即使全局偏好工作，也不获得工作工具。
- 输入区的旧模式切换改为权限菜单：请求批准、帮我批准、完全访问权限，分别使用手掌、带勾盾牌、警示盾牌；右侧标明选中项，完全访问为橙色。输入区统一透明底色、13px 字号、16px 图标与居中对齐，移除“了解更多”。新建配置默认帮我批准＋沙箱开启，终端仍关闭；旧 JSON 缺字段保留原策略，界面仅映射为三种名称。
- `/permissions ask|auto|full` 与 `/sandbox on|off` 修改当前会话；扩大权限需确认，任务或排队消息未处理时拒绝切换。全局、项目默认和会话覆盖独立保存，缺失新字段的旧配置保留原行为。
- 移除 Tab 和 `/` 菜单中的聊天／工作切换。Tab 或 Enter 接受预测，接受不会立即发送；保持 Enter 按住时的重复发送保护。
- AI 提示与状态共用一行：左状态、右侧浅灰小字提醒；随当前伙伴更新名字，仅完整对话显示此提示，快速对话保留状态行。减少底部间距，较长文字可悬停查看。

选择完全访问时，输入区菜单、斜杠命令和设置页共用应用内确认提示，按文件、终端、互联网与应用列出能力，说明风险及当前账户权限边界，默认键盘操作为取消；取消不写会话覆盖，设置页取消恢复原选项，已确认选择保存时不重复弹窗。当前任务入口放在侧栏“项目”分组中。

## 最终交付文件卡片

最终回复明确提供的本地文件链接／附件以整行卡片显示，顺序为正文、已编辑文件总结、交付文件、回复操作。复用用户附件的六项菜单，ZIP 等二进制只保留路径，另存为复制真实文件，不转成文字、不套用上传附件限制。文本复制读取实际文件；二进制复制文字入口禁用，缺失文件仍显示且可复制路径。历史保存 Markdown 原始链接即可恢复卡片，无需把文件内容加入后续模型输入。

专项测试 15 项通过（初始 14 项先失败后通过，随后补充二进制附件保护），覆盖 ZIP／HTML、六项菜单和回调、显示顺序、超过 4 MB 文件的原字节复制、UTF-8 内容读取、重复链接／网页／代码示例过滤、加密历史重开、完成前不展示、完成后不重复及缺失文件。没有自动启动用户默认浏览器或其他外部程序；实际另存为的字节验证调用了与菜单相同的复制后端。

## 实际隔离与审核

Windows 经典界面使用原生 AppContainer，每条命令拥有不同身份。项目可写、可选工具目录只读，终端子进程继承限制且没有网络能力；模型连接仍由宿主执行。Agent 的命令终端默认关闭；用户主动打开的手动终端按打开时的沙箱设置运行，后续设置不会静默放宽既有会话。完全访问不提供管理员权限。

原生测试实际启动系统 PowerShell、原生命令子进程及生产 `AgentCommandExecutor`，检查区内中文写入、区外读取与写入拒绝、子进程拒绝、回环 TCP 连接拒绝、超时、取消、六条并发命令和原 ACL 恢复。另检查临时用户目录和敏感环境变量不继承、只读工具目录、初始化失败不降级、硬链接拒绝。

独立审查发现的权限绕过均补充了针对性验证：父策略在创建 worker 前冻结；祖先搜索跳过宿主记录；已打开的无项目会话跟随实时全局默认；ACL 更新跨线程／进程加锁。实际测试还发现包 SID 的 Deny 不能封闭记录目录，改为在项目授权前永久停止宿主记录目录的授权继承，递归移除后代包 Allow。禁止通过项目根、工具目录、尾点或短名别名重新授予这些目录；拒绝重解析点与硬链接。普通命令、沙箱命令和 MCP stdio 均只继承本次三个标准输入输出句柄；自有事件探针验证不继承其他宿主句柄。

“帮我批准”由无执行工具的独立文字请求审核原始意图与准确操作；不确定、异常或超时转人工，明确拒绝不会自动重试，审计保存失败阻止继续。关闭沙箱的任意命令、未知 MCP、GitHub 写入和宿主目录打开保留人工确认。独立审核协议使用模拟模型验证，未调用付费真实模型账户。

## 多标签、摘要与过程展示

- 右侧统一承载文件、网页、ConPTY 终端、项目临时聊天和任务详情，不创建独立工具窗口。向左拖到边界并继续拖动，或按 Ctrl+Shift+F／视图按钮，进入完整标签视图；主对话固定为第一个不可关闭标签。返回分屏复用原主对话、草稿、滚动位置和最近工具选择。窄窗口自动改用完整标签，恢复宽度后自动返回分屏；显式选择的完整视图不因扩大窗口被取消。
- 标签采用白底选中卡片、细灰边框与轻阴影，未选中项使用灰色文字。图标、标题、关闭按钮统一基线，“＋”紧随标签。长标题省略并提供完整提示，标签较多时横向滚动。隐藏工具区保留进程和页面，关闭具体终端才释放对应进程树。
- 摘要为右上方圆角悬浮卡片，无贯穿窗口的分隔线；空间不足时自动收起，按钮可临时弹出，离开焦点后关闭。文件、差异、命令、子任务和完整摘要均可进入工具标签。
- 文件左键真实预览 HTML／Markdown／C#，六项右键操作保留；HTML 使用独立来源、本地资源校验和默认关闭的外部资源，验证 WebRTC 与跨帧绕过被阻止。浏览器仅由用户输入 HTTP／HTTPS 网址或关键词后初始化；权限请求与下载目前阻止并提示，未接入模型自动浏览工具。
- 手动终端是实际 PowerShell ConPTY + 本地嵌入 xterm，不是逐条命令模拟。两个终端的变量、目录和进程独立，支持持续输入、中文、Ctrl+C、尺寸调整；关闭标签结束所属进程树。真实沙箱终端验证项目内写入和区外访问／网络拒绝。
- 侧边聊天使用冻结的当前项目设置及独立临时上下文，不写主对话历史，不自动读取项目文件，不授予工作工具；关闭取消自己的请求。
- 过程状态带 20px 伙伴头像，适配减少动画；完成记录静态保留在回复按钮下，并保存原伙伴身份。历史区立绘采用用户确认的 20% 原始不透明度并渐隐，窄窗口减弱／隐藏，不铺入输入区；最低实测文字对比度 8.73:1，输入区截图像素保持一致。

## 验证结果

以下按具体测试目标列出实际结果，不把局部复测描述为全仓回归。初轮已有 36 个相关 Windows 目标通过；其后新增多标签、预览、终端等目标并复测受影响界面。最终标签样式完成后的主布局、真实终端、侧栏及相关对话检查均通过。

| 项目 | 已验证结果 |
| --- | --- |
| 完整／分屏与导航、滚动、草稿、隐藏、慢速拖动 | UnifiedWorkspaceTests 75 项；包含真实 WPF 截图 |
| 固定主聊天标签与释放保护 | UnifiedPreviewTabsTests 18 项 |
| 摘要显隐、分栏、标题、清理异常隔离 | SidebarWorkspaceTests 27 项 |
| 原生终端前端、双进程、切换布局与交互 | TerminalUiTests --native 24 项 |
| 原生 ConPTY 后端与沙箱／句柄／清理 | WindowsTerminalTests 22 项 |
| 实际 HTML／Markdown／代码预览与安全边界 | FilePreviewTests --html 28 项 |
| 内置网页浏览器 | BrowserPaneTests --native 27 项 |
| 任务摘要导航／临时侧边聊天 | 14／15 项；临时聊天使用自有模拟 API |
| 头像状态与完成记录／历史区立绘 | 32／30 项 |
| 最终交付文件卡片 | 15 项 |
| 主对话滚动、摘要适配、工具栏按钮位置与一键到底 | ConversationViewportTests 69 项 |
| 双处文件变更总结及持久化 | ChangeSummaryIntegrationTests --ui 40 项；ChangeSummaryPersistenceTests 21 项 |
| 托盘菜单、图标对齐与双击完整对话 | TrayInteractionTests 22 项 |
| 真实忙碌桌宠拖动、点击与动作恢复 | PetRuntimeTests 22 项；自有窗口实际鼠标交互 |
| 实际 AppContainer 与句柄继承 | WorkspaceSandboxTests 41 项 |
| 权限兼容、策略与审核 | WorkspacePermissionTests 38 项 |
| 双入口、权限菜单、底栏与快速聊天 | PermissionUiSmoke 51 项 |
| 预测输入含 UI／Markdown 渲染 | 49／94 项 |
| 完整／快速对话 UI／工作台 | 82／39 项 |
| 配置引导 | 58 项；17 章 130 个稳定步骤 |
| 共享 .NET 业务回归 | 355 项；共享源码最后修改后重新编译执行 |
| Avalonia headless | 此前 1604 项通过；本轮纯 WPF 标签样式未重复执行，不代表 macOS 实机验收 |

原生隔离、ConPTY 与 HTTP／WebView 模拟测试在 Codex 自身限制之外运行；限制内无法嵌套启动本测试 AppContainer，且 HTTP Listener／回环连接受限。环境失败单独记录，未计为应用通过。共享构建有 Windows API 分析及 NuGet 审计源不可达警告，经典 Windows 编译有 AgentTaskPanel 调度返回未等待及 TextProviderEditor CS4014 警告。

证据在 `work/permissions/`：`UnifiedWorkspaceTests-visual-final.log`、`unified-workspace.png`、`split-workspace.png`、`UnifiedPreviewTabsTests-tabs-final.log`、`SidebarWorkspaceTests-tabs-final.log`、`TerminalUiTests-visual-final.log`、`FilePreviewTests-unified-html.log`、`BrowserPaneTests-unified-native.log`、`windows-terminal.log`、`sandbox-native-final.log`、`pet-runtime-native-final.log`，以及 `integrated-target-results.json`、`native-integrated-results.json`、`shared-ui-native.log`、`avalonia-latest.log`。较早的日志与最后一次复测通过日志分别保留。后续证据包括 `PermissionUiSmoke-delivery-final.log`、`full-access-confirmation.png`、`ConversationViewportTests-delivery-final.log`、`ChangeSummaryIntegrationTests-delivery-final.log`、`change-summary-rebind-green.log`、`TrayInteractionTests-delivery-final.log`、`AgentWorkbenchSmoke-async-fix.log`、`SidebarWorkspaceTests-fixture-fix.log`、`SummaryNavigationTests-delivery-final.log`、`shared-delivery-build.log`、`shared-delivery-native.log` 和 `app-delivery-final.log`。

补充回归修复了附件读取以及差异预览、恢复／重试保存完成后的后台 UI 访问；界面更新显式回到 WPF Dispatcher。历史文件差异按所选记录校验，覆盖根目录与单文件备份路径，不因无关记录阻断当前文件。

## 切换对话弹窗的验证边界

复现了侧栏重复写入项目索引失败后异常逃逸至全局提示的路径，回归先失败后通过。侧栏现在委托激活，不再自行写索引或切换后刷新已卸载的导航。合成已保存会话完成 12 次 WPF 路由点击；当前预览数据的独立副本也做过切换检查，没有修改原会话数据。

用户当前预览中的原始异常尚未直接复现，不能断言上述路径就是唯一原因。新版本增加仅记录异常类型和代码堆栈的本地诊断，不记录异常消息、对话正文或凭据。本轮没有替换预览去收集其异常，也没有把未成功的物理鼠标探针计为通过。

## 交付与限制

独立构建位于 `E:\workspace\deskpet\outputs\workspace-permissions-local\桌面伙伴.exe`，不覆盖原安装目录。原预览 `E:\app\DafeiyuDeskPet\桌面伙伴.exe` 的 SHA256 前后保持 `A5E49A1EFE957BB54C0EF33DF1E647CCDC435682A41E7869B79B4880737F2B22`，修改时间仍为 2026-10-18 15:32:24。

AppContainer 不是虚拟机；系统向应用容器公开的运行资源仍可读取。文件、网页工具在宿主按策略执行，MCP 不在终端沙箱内。每个授权目录树上限 100000 项；工具依赖需显式配置只读目录，含链接或无法验证的目录会停止启动。没有为所有 Python／Node／Git 用户安装组合、真实模型、macOS 原生隔离或发布安装包作验证承诺。
