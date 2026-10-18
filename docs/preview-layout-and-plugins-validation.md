# 本地预览布局与插件验证

日期：2026-10-19。源码：E:/workspace/deskpet/agent-main-evolution，分支 codex/workspace-approval-sandbox。仅修改本地、备份替换并启动预览；没有提交、推送或发布。

## 已交付行为

- 主窗口默认1280×860逻辑像素并适配较小屏幕；新建完整对话窗口默认展开项目／对话侧栏，按钮仍可主动收起。永久图标列提供首页、用量、插件拼图、设置和头像；当前页为黑色实心。
- 设置“快捷键”更名“通用”；MCP移到独立插件页。Skills支持本地导入、搜索、启停、只读详情及确认移除，原始来源保留。工作请求和斜线菜单可发现启用技能，项目技能仍受项目信任控制；不会自动运行脚本或连接MCP。应用连接、插件包运行时已列入README TODO。
- 自动朗读置于头像和标题之间。摘要空间够时可点击固定并持续高亮；空间不足时临时展开且失去焦点后收起。侧栏隐藏不再暴露旧紧凑任务状态行。
- 品牌文字光学校准下移1.5逻辑像素；置顶／归档图标14px，点击区24px。插件顶部操作采用纯文字按钮。全局工具提示黑底白字圆角，上下文内容三行；未知容量不计算百分比。
- 内容框四角16px裁切；移除Alt系统焦点虚线和自定义持续蓝色焦点边框，保留悬停、按下与当前页反馈。

## 回归证据

所有路径相对工作树。WPF窗口测试串行运行，使用隔离数据，不连接真实模型。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| AppShellTests | 73通过 | work/preview-layout-audit/AppShellTests-final.log |
| PluginsUiTests | 40通过，含360/460DIP布局 | work/preview-layout-audit/PluginsUiTests-final.log |
| SidebarWorkspaceTests | 28通过 | work/preview-layout-audit/SidebarWorkspaceTests-final.log |
| McpUiSmoke | 8通过 | work/preview-layout-audit/McpUiSmoke-current.log |
| DetailedGuideTests | 58通过 | work/preview-layout-audit/DetailedGuideTests-current.log |
| ComposerPaletteTests | 4通过 | work/preview-layout-audit/ComposerPaletteTests-current.log |
| ConversationViewportTests | 69通过 | work/preview-layout-audit/ConversationViewportTests-current.log |
| UnifiedWorkspaceTests | 76通过 | work/preview-layout-audit/UnifiedWorkspaceTests-current.log |
| ManagedSkillLibraryTests | 41通过 | work/managed-skills-remove-transaction-green.log |
| .NET共享核心业务套件 | 退出0，无失败 | work/preview-layout-audit/shared-elevated-current.log |
| 经典应用构建 | 成功 | work/preview-layout-audit/build-final.log |

独立审查发现并修复技能导入／移除事务失败后形成孤立目录的问题。测试覆盖注册表写入失败、目录占用、回滚后重试、原始来源保留及失败时不发成功通知。启停影响下一请求；移除立即阻止未来load_skill读取，已返回的正文不撤回。

初次共享业务运行受沙箱回环网络限制，WPF测试也曾受原生剪贴板查询限制。已在允许本机夹具和桌面访问的环境重跑通过，未将环境失败隐去或当作通过。经典构建保留两条既有CS4014警告（AgentTaskPanel及TextProviderEditor）。

## 实际安装预览

- 安装位置：E:/app/DafeiyuDeskPet/桌面伙伴.exe。
- 备份：E:/workspace/deskpet/outputs/preview-backups/20261019-003346；包含原4个运行文件及完整data，112文件、784546009字节。
- 退出前检查输入框为空，向核对过PID归属的桌宠窗口发送正常关闭请求，等待保存退出；未强制结束进程。
- 4个运行文件与构建逐个SHA256比对成功。主程序SHA256：3AC14A80946B75AC2132442123DA3B2E1EB88553DD1200A6299E79B43EFA857B。
- 启动PID43696（仅为本次记录），默认实窗1600×1075物理像素；当前屏幕125%，工作区2560×1540。

实际原生截图及UIA边界：work/preview-layout-audit/initial.png、narrow.png（700×880物理像素）、sidebar.png、maximized.png、plugins.png、usage.png、settings-focused-final.png、alt-focus-final.png、summary-pinned-final.png、home-final.png。已人工查看默认／窄窗／最大化、品牌、插件纯文字操作、固定摘要以及Alt无虚线、设置无蓝色描边。

上下文实际控件可访问文本为三行，容量未知明确显示未知，见details-final.json。图标提示采用全局黑底白字圆角资源；实际屏幕捕获包含该样式的输入提示。朗读与上下文弹出层的单独截图未可靠捕获，不把相关文件当作独立弹出层验收证据。其具体样式复用已检查资源，文字由实际控件读取。

此次只做本机当前125%缩放的实窗检查，未声称覆盖所有显示器DPI、真实模型调用或第三方MCP服务器。没有为演示导入用户私有技能，没有发送模型请求。当前预览保持运行。


## 2026-10-19 追加界面回归（部署前）

插件、用量与设置主体使用900逻辑像素上限；设置保留5个真实分类，并提供分类搜索、图标导航与窄窗收缩。技能使用双列／单列列表，移除目录按钮；MCP 默认列表，点击顶部添加或行内编辑才展开表单，保存后手动连接。热力图在卡片内居中；单行图标文字使用自然行高；关于图标按最终反馈改为圆圈加感叹号。

本批次回归：AppShellTests 73、McpUiSmoke 60、PluginsUiTests 38、DetailedGuideTests 58、PermissionUiSmoke 51、WorkspacePermissionTests 38、ComposerPaletteTests 4、SidebarWorkspaceTests 28、UsageStatisticsUiTests 51、EmbeddedSettingsTests 13、SettingsNavigationSmoke 39，全部通过。日志保存在work/preview-layout-audit，各批次带batch-final或对应功能后缀。

权限界面测试原来假设初始侧栏收起，与用户已要求默认展开的行为冲突。测试现在显式收起侧栏再验证紧凑入口，宽屏阶段显式展开；未为满足旧断言更改产品行为。

设置最小窗口引导的一次可见性检查失败，重新运行含诊断与最终完整批次均通过；保留首次与最终记录。图片远程加载测试发现缺少同步上下文时后台线程直接更新WPF，已改为显式回到所属Dispatcher；不得用原失败结果声称通过。


## 最终追加验证与本地预览（2026-10-19）

图片预览38项通过：上传／助手附件、远程Markdown图片点击、原始图像与解码边界、16MiB下载上限、15秒超时、取消；真实ChatWindow内叠加而不新开窗口，背景模糊与原有效果还原，空白点击／Esc关闭，图片及工具条点击保留。视觉证据work/image-preview-evidence/image-real-chat.png、image-narrow.png。最终采用直接悬浮图片、轻浅背景、圆角白色标题与缩放胶囊。自动测试宿主不激活、不出现在任务栏，避免被误认为产品额外窗口。

热力图53项通过（UsageStatisticsUiTests-full-weeks-green.log）：365个真实日期按钮不变，前后非统计日期用不可选中占位补齐周列；未来日期标记尚未到来，不记为未采集或零用量。原始版新增完整周检查先失败，补齐后通过。月份标签不足3列间距时移除前一个部分月份标签，避免首部重叠。

用户报告切换对话弹出统一运行错误。保留的生产记录为2026-10-19 01:16:36本地时间，InvalidOperationException，堆栈位于Popup.GetChildInterestPoints／TransformToClient／TransformToAncestor，早于01:29预览。代码审查发现共享侧栏Popup关闭后立即清空并替换Child，在WPF延迟关闭期间不安全。已改为每次使用独立Popup、旧浮层保留自己的内容与目标直到WPF完成关闭，旧卡片事件校验归属；导航先Dispose再移除。未吞掉Dispatcher异常，也未把“重开即可”当作修复。

PopupLifecycleTests原版违反退休浮层保留视觉内容的检查；修正后42项通过，包含20次连续替换与布局重排。ConversationSwitchRegressionTests带开启的悬浮卡片连续切换36次通过；SidebarProjectInteractionTests 6、SidebarWorkspaceTests 28、AppShellTests 73通过。独立只读审查未发现重要生命周期／泄漏阻塞。上述检查没有稳定复现原始生产异常，不能据此声称所有运行异常均已消除。

最新部署：2026-10-19T01:42:47.1950124+08:00；备份 E:\workspace\deskpet\outputs\preview-backups\20261019-014241（4个运行文件与完整data）。部署前原预览已退出，启动PID37200（仅本次记录）。4个运行文件逐一校验SHA256，主程序 48CAF45B174B79DC9372D07F1995669733C0BF66F3CAEB1A2E3C51761CF24E4E。仅按用户要求将全局AgentApprovalMode设为Auto=2，保留其他字段与各会话单独权限。未提交、推送或发布。


## 引导首尾、搜索对齐与主动跑动（2026-10-19）

配置引导新增开场说明与祝贺页，保留原有步骤 ID 和恢复位置；跳过最后一章仍会到达祝贺页，只有点击“完成引导”才记为完成并打开已有完整对话。原生浅色圆角窗口使用自定义拖动区和关闭按钮，关闭可继续。用户先预览 GIF／独立交互页，再明确选择轻量彩屑；正式界面只播放一次十二片小彩屑，尊重系统关闭动画的偏好，不拦截点击。

GuideBookendTests 19、DetailedGuideTests 60、EmbeddedSettingsTests 13、OnboardingUpgradeTests 48、UnifiedPreviewTabsTests 18、BrowserPaneTests 14 项通过，日志为 work/preview-layout-audit 下 bookends-final 后缀。开场、祝贺、完成后聊天截图位于 work/guide-bookend-evidence，搜索框空白聚焦、输入文字、宽屏截图位于 work/search-alignment-evidence。新标签页搜索框保持 40 高度、自然行高与垂直居中的内容宿主，占位文字和输入光标对齐；Enter 路由验证无网络请求。未在真实用户数据上重开引导，以免清除已完成的教程进度。

轮盘和右键的“向左跑／向右跑”共用 PlayAction，现实际移动原生桌宠窗口。默认动作约两秒，以每秒 120 个逻辑像素按桌宠缩放及当前 DPI 换算，保留小数位置避免逐帧取整改变速度。只改变水平坐标，按当前显示器工作区限制；到边缘或结束停止并保存位置。自动漫步关闭、安静模式或对话处理中仍可主动跑动；位置锁定时保留原地动作。拖动、暂停、换动作／互动、隐藏、设置变更、换皮肤和退出均停止，不残留运动。

ManualRunTests 原实现先失败于右键跑动后原生位置不变；实现后 14 项通过，覆盖真实轮盘／菜单派发、左右位移、动作时长、左右边界、位置保存、锁定、暂停／拖动／互动中断、隐藏恢复，以及 held API 对话过程中继续位移与保留动作。InteractionTests 1005 项通过，App 构建成功；仅有原有两处 CS4014 警告。独立只读审查未发现重要阻塞。

最终本地预览更新于 2026-10-19T02:08:54+08:00，原 PID34344 正常退出，新 PID11880（仅本次记录）。备份 E:/workspace/deskpet/outputs/preview-backups/20261019-020849 包含四个运行文件与完整 data。四个运行文件逐一校验 SHA256，主程序 00FB6482BB5E04972BD84C2027B13C97700A4078E9F7341B14CEE3C39AA64EA5。保留所有设置和数据，本次不更改权限默认。没有提交、推送或发布。
