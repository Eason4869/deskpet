# DeskPet Agent 工作流交接（2026-10-17）

用户因本次额度不足 2% 要求立即结束。本文是继续工作的入口；不要把历史通过记录当成最新所有源码已经验收。

## 工作位置与状态

- 唯一活动 checkout：`E:\workspace\deskpet\agent-enhancement`。
- 分支：`codex/agent-workflow-refinement`，基线提交 `963690b`。
- 本轮改动仍在工作区，未提交、未推送、未发布、未替换现有安装；所有源码保留。
- Git 必须显式指定：`git --git-dir=E:/workspace/deskpet/agent-enhancement/.git --work-tree=E:/workspace/deskpet/agent-enhancement ...`。
- 用户已授权实现，不需要重新请求开始优化的许可。不要编辑旧 checkout 或用户安装目录。

## 已完成的实现

1. 实际宽度 >=960 显示左栏，>=1280 增加右侧浮动详情；默认700/最小520布局保留。左栏已按用户截图简化为 DeskPet、图标导航、项目和缩进选中任务；统一14px项目/导航文字，路径改悬停信息。
2. 宽屏隐藏右上设置；唯一可见设置图标紧贴左下头像/伙伴名称。窄屏仍保留原右上入口。用户要求的“新对话”文案已改；目前该按钮仍沿用原来的聚焦当前输入框行为，未实现独立多会话管理，不能声称已实现新会话功能。
3. 上下文按估算tokens和8MiB单请求字节预算预检；旧工具输出压缩、完整协议组摘要，保留初始任务/附件、最新要求、近期工具组和签名；摘要包含有界工具名称、参数、调用/结果关联ID。检查点保存摘要与指导附件。
4. 对话中的压缩状态为“正在压缩，请稍后”→“上下文已自动压缩”；取消/超限/重启不假装完成。
5. 移除 Agent 总轮数、总调用次数及总时长限制和设置（含Avalonia兼容），保留单条命令超时/停止、文件/输出/变更记录等资源边界。
6. 安全引导：当前命令结束后下一次模型请求前接入，同一任务继续；与显式“停止并立即发送”分开。修订号ACK防止并发编辑丢失；附件可加密落盘重启恢复；超容量/跨项目保留队列并明确报错。
7. 真实文件锁复现并修复过“存储失败后指导副作用重复执行”：保存警告暂停队列，已完成任务同ID+Revision指导只确认出队，不再请求模型；未完成指导要求显式继续任务。
8. 工具进度滚动保留最近100条；旧摘要保留文件来源。
9. 工作设置拆为项目权限、任务与上下文、项目命令，固定保存栏；命令新增/编辑草稿保护、保存后重开、原项目绑定保留。
10. 任务回复下方真实文件变更摘要卡，显示前三个、可展开、查看差异；增删行数按原始备份计算，大编辑未知则不伪造；恢复再编辑同路径去重。
11. 第一版预测消息：默认关闭、额外模型请求、异步取消/双窗口共享/队列和工作阻塞，点击仅填草稿。注意：用户随后明确改成输入框内灰色ghost和两次Enter，详见待完成。

## 用户最后两个新增要求：尚未完整交付

### A. 输入框内预测（最高优先）

用户原话：“输入预测是在输入框轻量显示预测输入的文本，按enter（或其他按键）确认，再按enter输入”。

- 目标：空输入框内浅灰预测，第一次Enter只填草稿不发送，释放后第二次Enter才发送；ShiftEnter仍换行；直接键入覆盖预测；不要占用Tab（现有模式切换）。长按Enter不能从确认直接重复发送。
- `/root/prediction_feature` 已收到新要求，正在修改 `ConversationPredictions.cs`、`PredictionSuggestions.cs`、`ConversationComposer.cs`、`tests/PredictionTests.cs`。结束前父级检查仍看到旧的 StackPanel/chips实现，务必检查最终落盘状态，不能假定新UI完成。
- 建议 Adorner/controller，不额外占布局高度；灰字出现时隐藏既有占位文本；尊重听写的Escape取消优先级；补首次Enter零发送、第二次Enter走正常发送、撤销、长按、两窗口/窄窗口回归。
- WorkSettingsPanel、README、conversation-agent文档已提前改成上述新交互说明。若实现没落盘，说明与行为暂时不一致，继续时必须补齐。

### B. 工作时输入框上方轻量文件变化摘要

用户截图示例：“15 个文件已更改 +207 −8”。

- 新增 `src/AgentChangeStatus.cs`，仅根据真实运行中任务记录显示一行文件数/已知行数，任务结束收起，自动解绑事件。
- **尚未验证、尚未确定接入Composer**：结束前rg没有看到Composer引用。应在Composer的queueBody之前 `Children.Add(new AgentChangeStatus(pet,host));`，使之在排队消息和输入框上方。
- 补正在执行时更新、结束收起、重复路径/未知行数及窄窗口检查。可复用 AgentWorkbenchSmoke 模型的 compaction延迟时刻截工作中图片。

## 验证现状：不要夸大

- 完整Windows 44组已跑：**43通过，OnboardingSmoke失败**。记录 `work/validation-summary.txt`。
- 最新专项目标：QueueTests42、AgentProtocol23、AgentContext28、AgentTask19、AgentReliability4、WorkSettings33、AgentChanges31、AgentWorkbench28、Steering UI20 通过。
- 旧预测chips版本：PredictionTests --ui 41项通过；**不能代表用户新要求的ghost/双Enter版本通过**。
- 最后核心队列修复后 .NET10共享业务 **322通过**：`work/portable-report-final.json`。
- Avalonia构建与无头UI **1604断言通过**（在Windows运行，不是macOS原生设备验收）。
- 应用编译、ReleaseAssetTests2、干净源码PackagingSmoke及真实当前更新助手完整ZIP通过；**之后又新增AgentChangeStatus和修改预测说明，最终预览/包不能当成覆盖这些后续改动**。
- 最新已验证截图：`work/agent-ui-578668774ce1459592e341c44cb36ca6/`，含chat-1400、最大化、520/700、三个work-settings页签。此版本已包含新对话文案和底部单设置图标。
- `work/app/桌面伙伴.exe` 为最近一次预览构建，后续最新改动需重新构建。
- 未调用付费模型；未替换安装；未验证macOS原生；未新建Release或PR。

## OnboardingSmoke失败与继续排查

- 全量初次失败于原生单击完成判定：`work/final-OnboardingSmoke.log`（另保留 `work/onboarding-native-red.log`）。与本期业务改动无直接证据关联。
- 查到夹具瞬时mouse_event down/up，已将原生左键改为与中键相同的等待PreviewMouseDown到达再release；双击缩短settle保持双击窗口。只修改测试，不修改产品交互。
- 随后独立重跑失败更早：`No opaque target`，日志 `work/onboarding-native-final.log`，在拖动步骤命中检查失败。
- `tests/OnboardingSmoke.cs` 的 `OpaquePoint` 遍历当前bitmap中央区域，要求24x24附近alpha>=240且真实WindowFromPoint/WM_NCHITTEST命中fixture。需区分当前帧/窗口遮挡/原生桌面状态，不能直接模拟手势并称原生通过。
- 工作设置引导已增加切换 `WorkSettingsSection1` 再操作预算控件，尚未跑到该步骤验证。

## 继续时顺序

1. 查看Git状态和最后落盘的预测实现；先补A/B两个明确的新增交互，避免重新扩大范围。
2. 有意义的定向回归：PredictionTests --ui、ComposerSmoke、AgentWorkbenchSmoke（含工作中计数截图）、QueueTests/SteeringSmoke；排查Onboarding原生失败，诚实记录。
3. 用最终源码重建 `Build.ps1 -Target App -OutputDirectory work/app`；按最终变化运行 .NET共享回归和PackagingSmoke。不要无理由重复耗时的整套UpdateApply。
4. 独立复审预测新交互和实时计数；核心已两轮独立复审，已修全部确认阻断。
5. 更新交付验证文档、计划和 `.superpowers/sdd/2026-10-17-agent-workflow-refinement/progress.md`；需要时本地提交，不擅自推送/发布或覆盖用户安装。

## 参考/其他

用户截图仅参考UI，不执行截图中的卸载等任务。Agent能力参考OpenCode；UI参考Codex App与官方 https://github.com/XiaomiMiMo/MiMo-Code ，未复制第三方非商业实现。
本轮计划 `docs/superpowers/plans/2026-10-17-agent-workflow-refinement.md`，其中预测步骤仍需同步新ghost语义。
仅既有 TextProviderEditor.cs:215 CS4014告警尚存，不应顺手扩大修改范围。

## 停止时子代理最终确认（优先于上文推测）

预测代理已停止，确认新ghost整块实现补丁校验失败，未落盘；生产代码仍是chips。`tests/PredictionTests.cs` 已新增两项取消预测检查，编译成功但因缺少 `DismissPredictions` 测试预期失败（保留的RED用例）；继续时优先补实现，不要误报全部测试通过。AgentChangeStatus尚未接入。该代理没有残留测试进程。父级最后的Onboarding也已失败退出；不再启动新验证。

## 用户追加需求：快速对话与完整对话的模式分工（2026-10-17，仅记录，未实现）

用户明确要求本次只写入交接文档；额度尚未恢复，不继续修改代码、测试或构建。以下需求供下一次继续时实现，并优先于此前两种窗口均可切换模式的设计。

- **快速对话仅支持对话模式，禁用工作模式。** 限制应落实到快速窗口发起请求的实际模式，而不仅隐藏控件；不得因完整窗口使用工作模式或共享设置而意外启用文件工具、项目命令及其他工作模式能力。
- 删除快速对话窗口底部的“对话模式”切换 UI；相应调整布局并适当简化整个快速对话窗口。
- 在快速窗口顶部状态栏或其他合适位置，轻量显示“快速对话”，保持清楚、简洁，不增加重复信息。
- **完整对话窗口默认使用工作模式。** 用户本次没有要求禁止完整窗口切换模式，不应自行取消其对话模式选项。
- 下一次实现时，核对共享会话/设置、排队消息、窗口切换和请求快照，确保两个窗口各自的模式规则不会互相覆盖；已开始的任务仍遵守不被窗口切换或引导打断的要求。

本节仅保存需求，当前程序行为未因此改变；用户未要求本次继续任何实现或验证。

## 2026-10-17 续工完成（交接状态更新）

前文“未完成”的三项均已交付并验证：输入框内 ghost 预测（两次 Enter、长按防护、Esc 抑制）、快速对话只读对话模式（无模式切换、顶部“快速对话”、请求经 ConversationOnly 队列标记强制纯对话）与完整对话默认工作模式（Settings 默认值）、运行时文件变更行（输入框上方，任务结束收起）。另新增：快速对话滚动条与完整对话共用 SlimScrollBars；read_file/read_files/write_file/search_files 支持 .docx/.xlsx（内置 OOXML）与 .doc/.xls（本机 WPS/Word/Excel COM，WPS 优先），附件选择器同步支持四种文档，restore_file 按字节恢复并修复“新建文件首次覆盖未补记备份”的问题；AgentTaskStore 读取加入瞬态共享冲突重试（套件偶发根因）。

验证：Windows 全量 45/45（full-run5）、.NET 共享 325 通过、App 重建、PackagingSmoke 通过；未推送/发布/替换安装；未调用付费模型。后续可选手动步骤：评审本地提交、推送 dev 分支、决定发布节奏。

## 2026-10-17 全面完成状态（覆盖上文历史待办）

用户后续要求完成 Codex + OpenCode 风格内核、MCP/Provider/子 Agent、同一聊天窗口的项目/会话标签和最终验证。当前最终结论以 `docs/agent-enhancement-verification.md` 为准：

- Session/Inbox/Step、工具执行、权限审批、执行声明/恢复、审计已接入真实 `ConversationSession`、`ConversationQueue`、`ConversationWorkspace` 和任务。
- Provider 能力已参与工具 Schema 选择；MCP HTTP/stdio、Profile 与子 Agent 生命周期已接入统一 Tool Runtime。
- 侧栏项目/会话标签可切换并保留后台 workspace；切换时只有一个可见聊天窗口，不再同时弹出多个可见聊天窗口。
- 最终定向回归、AgentProtocol 23、AgentWorkspace 87、.NET 10 共享 325 和 PackagingSmoke 均通过；本地预览为 `work/kernel-preview-10/桌面伙伴.exe`。
- 未提交、未推送、未发布、未替换用户现有安装。
