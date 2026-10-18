# 统一主窗口与用量统计：本地验证记录

日期：2026-10-18。源码位于 `E:/workspace/deskpet/agent-main-evolution`，分支 `codex/workspace-approval-sandbox`。实现用户已确认的设计，并纳入后续图标、菜单、历史入口及圆角反馈；没有推送、发布或替换已安装预览。

后续状态：2026-10-19 已完成新一轮侧栏／插件／技能与视觉修正，并已替换安装预览。本文保留18日快照；当前验证及安装信息见 [预览布局与插件验证](preview-layout-and-plugins-validation.md)。

## 可见结果

- 完整对话、设置、用量统计共用 ChatWindow，设置抽为 SettingsPage；旧 SettingsWindow 只作测试兼容。顶栏不显示系统标题或窗口名，提供返回、前进、侧栏开关、四组命令菜单与原生窗口控制。
- 最左图标列常驻：首页、八齿齿轮、底部伙伴头像。用量统计只从头像菜单进入；没有虚构账号、套餐和余额。主内容框四角 16px，并随尺寸变化更新 RectangleGeometry 裁切，子页面背景不会盖住圆角。
- 项目／对话侧栏 Ctrl+Shift+S 收起；窄窗口临时展开、Escape 收起。页面切换保留聊天、草稿、设置编辑、工具及进程。
- 新建对话采用气泡加号；完整标签／返回分屏与显示／隐藏工具区采用不同图形和独立状态。移除聊天右上重复设置；导出进入文件菜单，加载更早历史只在有记录时出现在滚动区顶部。
- 用量概览、每日／每周／累计热力图、7／30天堆叠图、筛选和会话排行。深色圆角双行详情支持鼠标和键盘，说明日期、区间、总量与估算／未知。
- 只采集文字模型请求（包括辅助请求），不包含 ASR、TTS、Live；按请求落盘，实值优先，未知与零分开，缓存／推理不重复计入总量，不回填无法恢复日期的旧汇总，不保存提示词／回复／密钥。
- 帮助提供 GitHub 主页、CHANGELOG、Issues 和可搜索快捷键弹窗。菜单与快捷键来自相同命令表，编辑动作只针对可见可编辑控件。

## 实际验证

所有 UI 测试串行运行，使用自有临时目录；HTTP 仅使用本地模拟接口，无付费模型请求。下面日志均在源码目录的 `work/permissions/`：

| 检查 | 结果 | 日志 |
| --- | --- | --- |
| 统一窗口、页面路由、草稿、圆角、窄侧栏、编辑目标、快捷键、原生窗口控制 | 35通过 | AppShellTests-shell-final.log |
| 同窗口设置、未保存内容、实际Owner | 13通过 | EmbeddedSettingsTests-shell-final.log |
| 统计筛选、聚合、空状态、持久化提示、点阵鼠标／键盘详情 | 51通过 | UsageStatisticsUiTests-shell-final.log |
| 快捷键搜索、空状态、键盘关闭 | 12通过 | ShortcutHelpTests-shell-final.log |
| 用量采集经典 / 共享.NET10 | 22 / 22通过 | UsageTrackingTests-shell-final.log / UsageTrackingTests-shared-final.log |
| 视口、滚动条、摘要、按钮边界、一键到底 | 69通过 | ConversationViewportTests-shell-final.log |
| 工具标签、切页、滚动与草稿保留 | 76通过 | UnifiedWorkspaceTests-shell-final.log |
| 不重复的分屏与工具显隐控制 | 28通过 | TabLayoutControlsTests-shell-final.log |
| 原生ConPTY+WebView2、两终端与布局保留、沙箱输入桥 | 24通过 | TerminalUiTests-shell-final.log |
| 工作对话 / 托盘 / 权限入口 | 39 / 22 / 51通过 | AgentWorkbenchSmoke / TrayInteractionTests / PermissionUiSmoke-shell-final.log |
| 既有对话界面兼容 | 82通过 | ConversationUiSmoke-shell-final.log |
| 共享业务工程构建与回归 | 退出0；0编译错误 | shared-shell-build-final.log / shared-shell-final.log |
| 独立Windows应用构建 | 退出0 | unified-shell-build-final.log |

审查中已修复：未知pending记录阻止后续估算、设置Live测试返回页面、隐藏输入框成为编辑目标、新聊天丢失当前聊天／工作入口、隐藏工具再返回分屏抢选聊天、恢复工具按钮越过阅读边界。最终补做回归发现延迟自动到底覆盖更新的阅读位置；先记录失败，再增加会话及位置校验，76项工具工作区与69项视口测试通过。诊断输出已移除。

## 实际截图

- [统一首页与四角裁切](../work/permissions/app-shell-home.png)
- [窄窗口与常驻图标列](../work/permissions/app-shell-narrow.png)
- [设置页面](../work/permissions/app-shell-settings.png)
- [用量独立页面：真实空状态](../work/permissions/app-shell-usage.png)
- [实际命令表的快捷键弹窗](../work/permissions/app-shell-shortcuts.png)
- [统计样本](../work/usage-overview-fixture.png)、[分析样本](../work/usage-analysis-fixture.png)
- 悬浮详情样本：[每日](../work/usage-heat-tooltip-daily-fixture.png)、[每周](../work/usage-heat-tooltip-weekly-fixture.png)、[累计](../work/usage-heat-tooltip-cumulative-fixture.png)

带 fixture 的统计图片来自自有测试数据，不代表真实消耗；统计页实用时只读取本机新采集请求。测试截图不是设计草图。

## 限制与交付边界

已验证当前桌面环境中的宽窄布局和原生最小化／最大化／还原。尚未切换操作系统125%／150%缩放或多显示器迁移验收；没有真实服务商账单、生产付费请求与实际麦克风／通话验收。编译保留已有的未等待异步调用警告；共享工程构建0错误。没有将这些检查解释为全平台或人工验收。

产物是 `E:/workspace/deskpet/outputs/unified-shell-local/桌面伙伴.exe`，包含同目录更新辅助文件及配置；保持整目录使用。没有启动此产物或已安装预览。已安装预览 `E:/app/DafeiyuDeskPet/桌面伙伴.exe` 的长度133368832、修改时间2026-10-18 15:32:24以及SHA256 `A5E49A1EFE957BB54C0EF33DF1E647CCDC435682A41E7869B79B4880737F2B22` 保持不变。

本地构建长度：135389184 字节；生成时间：2026-10-18 23:16:08；SHA256：`F09A9D27E7B41CE3F666EFE8DCE88862F7F3DF69A9A10D530AD17EF367FB5DA6`。

最终 git diff --check 退出0；已清理采集调用点的行尾空白。测试窗口与所属终端均已释放。
