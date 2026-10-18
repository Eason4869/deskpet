# 本地全量核对（配置引导重做前）

> 历史审查记录：以下本地状态、版本及测量对应 0.6.3 预览阶段。相关修改现整合进 0.7.0；当前功能及交付状态见 README 与 CHANGELOG。

范围：Windows/.NET Framework 桌面端当前全部本地改动、共享核心、现有测试与跨平台入口。未提交、推送或发布。原始证据：`work/audit-part-*.json`、`work/audit-*.log`、`work/audit-summary.json`，受影响功能的复验为 `work/final-*.log`。

## 已修正

- 已配置模型容量仍被工作设置默认 32768 上限限制：运行预算现直接采用模型容量，自动预留输出与协议空间；只有容量未知时使用工作设置兜底值。预览与实际请求共用该策略。
- 删除项目命令 UI 后，历史配置仍可能授权执行：桌面 Session 不再载入这些授权，schema 不公布命令，执行入口也拒绝；保存工作设置清除该范围的旧授权。
- 模型看到的工具与桌面可执行能力不符：桌面过滤未连接的 GitHub 读取、尚无确认提供器的 GitHub 写入，以及只有生命周期记录、没有执行 worker 的子 Agent 工具；直接调用也拒绝。底层可测试的工具接口保留，不冒充用户已经配置的功能。
- 项目页的执行过程、预测消息不会保存到项目：明确属于全局偏好，在项目编辑时只读，避免产生已保存的错觉。
- 查看项目备份曾使用全局目录：现在使用当前编辑范围的已保存设置。
- 设置页已改为单页分组，显示全局或项目范围；移除三个旧标签页和命令编辑器；补全无文字开关的辅助功能名称。

## 后续优化机会

1. 模型工具、图片、音频能力仍部分按厂商/名称推断，通用兼容服务应支持显式能力配置与失败反馈；不要靠已知名称表判定一个实际支持工具的模型不可用。
2. GitHub 写入需要真正的确认界面；MCP 需要连接、状态与权限管理；子 Agent 需要独立模型执行 worker、结果验证和取消传播。当前不宣传这些已完整可用，也不扩大权限绕过缺失环节。
3. 配置引导已按下文重做并复验；后续应继续随真实功能更新离线版与小步骤，避免文案与控件脱节。
4. 原生输入测试需要串行运行、获得焦点后确认命中；动画逐帧测试不能依赖系统在高负载下每个定时帧都被显示。
5. Avalonia 工作设置界面仍有旧上下文预算文案与 128K 输入上限，后续应同步 Windows 端策略与交互。共享实际预算逻辑已同步，本次用户测试入口仍为 Windows WPF。
6. 上下文用量目前是估算，并受请求字节上限约束；后续可接入服务端 usage 和按模型分词估算，但不可将估算标为精确计费。
7. 引导重做后还需维护非 CI 的历史烟测：旧版控件名称、快聊会话复用和旧动画图集的断言已经失效，应按现有真实操作更新。

## 验证与限制

共尝试 90 套本地测试；ComposerPaletteTests 在隔离复测通过后，82 套通过。其余 8 套需要夹具更新、参数或独立原生输入复测，不能当成通过：

- MotionSmoke：仍要求旧图集和旧帧数，和目前独立跳跃素材不符。
- AutoUpdateSmoke：查找已移除的当前模型 ID 字段，尚未适配新模型页。
- LiveCallSmoke：仍依赖旧的快捷会话/草稿共享行为。
- XiaobeiUiSmoke：需要明确传入已批准 pet.json，裸运行只返回用法。
- MenuSmoke / ActionPaletteSmoke / InteractionUiSmoke：并发窗口引起鼠标命中或焦点检查失败，需隔离环境复验。
- StumbleSmoke：实际渲染的帧采样在并发高负载下漏帧；对应状态时钟和素材几何另有确定性回归。

相关功能复验：WorkSettingsTests 36；AgentProtocolTests 23；AgentWorkbenchSmoke 38；ContextUsageTests 18；SettingsMaximizeTests 5；ComposerPaletteTests 4。共享核心结果见 `work/audit-portable-report.json`。未执行真实麦克风 AudioSmoke，不把模拟语音测试当成真实设备验收。无真实付费模型调用。

## 完整对话首次打开的项目选择修复

桌宠入口首次打开完整对话原先没有会话工作区，因此工作模式也不显示项目选择。现在首次打开与界面内新建共用草稿创建逻辑，发送前不加入已保存记录。项目栏及选择权限按当前记录的实际模式判断；切换模式创建对应的独立草稿，保留未发送文字，清除旧项目选择；对话模式使用当前桌宠的快速对话归属。

补充验证：ConversationModeUiTests 6 项通过（首次打开、对话模式隐藏、重复切换、关闭后重开、清除旧项目、已保存工作记录）；ConversationSingleWindowSmoke 7 项通过；ConversationOwnershipTests 6 项通过；ComposerPaletteTests 4 项通过。应用已编译到本地 staging，尚未替换当前运行实例，也未发布。记录归属与引导仍需完成后续整合核对，不能将这些定向回归视为整批工作的验收。

## 配置引导重做与欢迎页补充

引导现为 17 章、113 个稳定 ID 小步骤，仍定位真实设置控件，增加章节目录、返回、跳过本步／本章和精确断点恢复。关闭窗口保存为稍后继续。系统朗读与云端 TTS 按开关分支；云端从明确开启、服务商、地址、协议、模型、独立 Key、音色、代理，到保存、显式试听、停止和排错逐项说明。保存、实际测试和跳过分别记录；试听取消、识别失败、Live 连接失败不计为成功，重新保存服务会清除旧验证。文字测试、录音、播放和通话均须用户显式触发。

完整对话章节使用真实窗口练习拉宽／最大化、显示侧栏、创建分区、创建项目与对话、切换模式与选择项目。小屏幕仍可跳过侧栏练习并使用项目选择器。工作设置增加模型容量自动管理、未知容量兜底、真实 Agent 边界与共享工作提示词的细分说明。离线版见 `docs/configuration-guide.md`。

工作模式的新对话保留当前伙伴头像、欢迎插画、标题和快捷建议。建议先根据模式、项目与相关记录标题生成；存在同范围已打开历史时，复用已有的有界、缓存模型预测，不自动打开全部历史或附加文件。普通聊天只引用当前伙伴，工作预测限定当前项目；点击建议只填草稿。预测默认对新配置开启，显式关闭的历史配置保持关闭；关闭后使用中性默认建议，不发起预测请求。

本轮定向回归：DetailedGuideTests 50；OnboardingModelSmoke 55；OnboardingSmoke 72（模拟手势，不是实际鼠标验收）；GuideMenuSmoke 8（独立原生菜单）；OnboardingUpgradeTests 48；WelcomeSuggestionTests 10；PredictionTests 32；DictationTests 10；LiveTests 29；CoreTests 16；ProfileTests 86；ConversationModeUiTests 6；ComposerPaletteTests 4；WorkSettingsTests 37；SettingsMaximizeTests 5；DraftConversationTests 5。所有上述检查通过，应用编译成功，`git diff --check` 通过。语音使用模拟设备及本地 HTTP/WebSocket 夹具，未将真实麦克风或真实云端账号标为已验证。

本地 staging 已更新，当前运行实例尚未替换，无提交、推送、远端发布。此前全量核对中的其他未通过项目仍保留上文限制；记录归属整合的最终结果见下文。

## 本地最终整合复核

普通聊天和快速对话的会话、记忆、草稿及附件按伙伴隔离；工作记录与工作提示词共享。固定“快速对话”位于自定义分区后、“最近”前，二者均仅支持展开收起，不提供编辑入口。已保存工作会话切换伙伴时继续原记录，新工作对话保留当前伙伴欢迎插画。底部设置按钮靠右，与头像中心对齐。

旧目录中的独立历史会导入可见记录，混合历史按完整轮次分开工作／私人内容，保留原加密文件和迁移备份。新增幂等导入标记。损坏目录读取后禁止用空目录覆盖。历史原子保存使用同目录短临时文件名，避免有效目标路径因追加长后缀而超限。

活动状态的浮光改用相对坐标移动；编辑、运行等显示对应矢量图标，完成／卸载后释放动画。单条队列的拖动手柄隐藏且不可聚焦；增为多条时悬浮显示，删除回单条时重新隐藏，右侧引导、编辑、删除仍可用。附件异步读取会核对会话草稿归属，避免切换后填入别的会话。

最新整合检查：QueueHandleTests 11、HistoryTests 21、ConversationOwnershipTests 14、ConversationOwnershipUiTests 15、ConversationVisualTests 28、ConversationModeUiTests 6、SidebarSectionsTests 7、DraftConversationTests 5、ProjectCatalogTests 5、AttachmentPickerTests 4、QueueTests 47，全部通过。共享核心 330 项通过（本地 HTTP/WebSocket 夹具在允许本地连接的环境重跑；沙箱网络限制引起的失败不作为产品结论）。证据见 `work/latest-regressions.json`、`work/latest-ownership.log`、`work/latest-portable-report.json`。

最后复验引导：DetailedGuideTests 50、OnboardingModelSmoke 55、OnboardingSmoke 72（模拟手势）、GuideMenuSmoke 8、WelcomeSuggestionTests 10，全部通过；见 `work/last-guide-results.json`。无真实云端服务或麦克风验收。新编译程序仅用于本地继续测试，不提交、推送或发布。此前全量核对中列出的旧夹具／原生输入等限制继续保留。
补充：旧 ConversationUiSmoke 已适配图标标题和草稿计划设置范围，前述消息显示检查通过；末段仍使用模式切换前的模拟会话寻找发送结果，返回“序列不包含任何匹配元素”。该整套烟测未通过，保留为待维护夹具，不计入通过数量；独立 ConversationVisualTests 与 ConversationModeUiTests 的通过结果不受其影响。

本地预览已替换并启动：PID 38252，SHA256 `3137D606471E1A26B42336B84EDE11CE7E779C96F377C98E1DE48E1BBF29D1E6`，备份在 `E:\workspace\deskpet\preview-backup-063-20261018-133326`。应用与数据均保留本地，未发布。