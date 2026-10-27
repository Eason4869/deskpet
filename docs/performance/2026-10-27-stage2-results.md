# 第二批性能优化结果

日期：2026-10-27。继续使用 `codex/performance-0.11.2-dev.2`，本批起点 `dc39043`，仍基于用户指定的本地 `0.11.2-dev.2`。没有更改版本或发布。

## 实际改动

1. WPF AI 五类编辑器真正按需创建。文字页不创建 ASR、Speech、Live、Image；直接进入 Image/Live 也不先创建文字编辑器。Avalonia 模型组内部 text/image 同样按需创建。
2. Save/Guide/脏状态读取处理未创建编辑器，保存未访问服务时不重建或覆盖其配置；所有已访问 Provider 列表先完成读取/校验，再应用，覆盖“无效的非当前 ASR Provider 不得部分修改文字草稿”。
3. Provider 基线与原生编辑器数据改为 ServiceProviderSettings 专用副本，移除全量 PetSettings.Copy；删除文字配置变化触发无关语音列表刷新的订阅与无实际读取的临时 view.Text 赋值。
4. WPF 缩放、音量连续输入立即预览，普通提交合并；拖动期间暂停已有 timer、保留 dirty，释放后只保存一次；明确保存、恢复默认、语言、维护与退出仍正确。Avalonia 普通偏好写盘合并尚未在本批实现。
5. 窗口缩放几何独立应用、同周期合并位置回调，不再为每个滑条值执行全量 ApplySettings 和一笔额外位置保存。原子替换、写锁与错误提示沿用现有 SettingsStore。

最终一次持久化仍在 UI 上同步执行，本批没有实现全套后台异步保存协调器；没有把旧完整设置快照排队，从而避免新增过期快照覆盖风险。

## 性能对照

同一 Windows 会话、相同 `--measure` 默认隔离数据，10 个设置实例、30 次伙伴/文字页切换，无强制 GC。测量前结束其他构建/UI 测试。该工具不覆盖完整进程启动或 OS 实际呈现。

| 指标 | 第一批完成后 | 本批完成后 |
|---|---:|---:|
| 设置构造中位数 | 22.623ms | 21.035ms |
| 设置首次 render callback 中位数 | 239.093ms | 245.901ms |
| 包含首次 AI 的切换中位数 | 14.730ms | 14.776ms |
| 包含首次 AI 的切换 P95 | 35.841ms | 24.180ms |
| 包含首次 AI 的切换最大值 | 542.036ms | 347.350ms |
| 末尾 Private Bytes 单点 | 359763968 | 350932992 |
| 末尾 Working Set 单点 | 486576128 | 482340864 |

AI 切换最大样本下降约 35.9%，P95 下降约 32.5%；温热中位数基本相同。首次 AI 初始化仍存在可感知开销，不宣称已完全消除。后续应继续分段采样文字编辑器内部 UI/首次 JIT/布局，而不是笼统宣称所有页面都更快。

单独冷构造 profiler 使用相同配置，AI panel 从 1354.388ms 到 1095.388ms，初始化编辑器数量从 5 个变成 1 个；此值包括首次类型/JIT 等因素，与切页渲染计时不是同一个指标。

内存单点不是稳定保留堆和泄漏结论。额外的受控弱引用测试证明：WPF/原生编辑器存活时，已从 Settings 移除的无关 100000 字符指令缓冲可以回收；同时验证 clean 外部刷新与本地草稿保护。不能将单点差值或这一个字符串测试外推为整机内存节省比例。

## 行为验证

| 项目 | 结果 |
|---|---|
| WPF AI/页面生命周期 | 34 通过；包含未创建页、未访问服务保存、直接 Image/Live、滚动复用、无效非当前 Provider 校验 |
| 普通偏好保存 | 21 通过；缩放/音量 held 无写、release 一次、重复拖动、held 退出、显式保存/语言/维护/恢复默认、失败重试 |
| WPF Provider 内存 | 3 通过，受控可达性检查，非性能 GC |
| Avalonia 生命周期与内存 | 15 通过 |
| Avalonia 导航 | zh_CN 24、en_US 24 通过 |
| Avalonia IM / 下载弹窗 | 7 / 8 通过 |
| EmbeddedSettingsTests | 13 通过 |
| SettingsNavigationSmoke | 66 通过 |
| SettingsFeatureNavigationTests | en_US 44 通过 |
| LiveLanguageUiTests | 61 通过 |
| WorkSettingsTests | 67 通过 |
| OnboardingModelSmoke | 本机 HTTP fixture 在批准的沙箱外运行，56 通过 |
| ProviderUiSmoke 无设备模式 | 83 通过；显式 `--without-device-playback` 不验证真实播放和该预览段的 Live 链路 |
| 独立代码审查 | 发现 1 项 P2：已有 timer 在第二轮拖动时写盘；已补 RED→GREEN 修复，相关保存回归 21 通过 |

新增测试均确认对应旧路径失败后实现。审查后补充 held dirty/退出回归，证明拖动时仅暂停持久化，不丢失 dirty 状态。没有以删除失败断言或修改生产前台检测器让环境测试假通过。

## 尚未通过的环境验证

- 默认 ProviderUiSmoke 的真实 Speech 播放完成等待失败。为排除本批回归，使用 `git archive dc39043 src` 与同一测试编译独立基线对照，批准后在相同环境执行：同样收到 `/v1/audio/speech`，随后停在 `busy=True, speechRevision=0`。真实设备媒体完成未通过；默认严格测试保留，另增加显式无设备模式覆盖其余配置/模型发现回归。
- DataMaintenanceHostTests 在沙箱内外均未取得预期前台窗口，生产 fullscreen 检测为 true，失败发生在“初始空闲”环境前置条件。该测试不能计为通过；新增偏好保存测试中的维护阻止写入、维护后保存、退出失败保护已通过。完整原生主机验收仍需可用交互桌面。
- 未实测 macOS/Linux 原生图形后端；未完成长期稳定堆、完整冷启动与真实网络角色下载验证。

## 原始证据与预览

日志在工作树忽略目录 `work/performance/`：`stage2-ai-profile-baseline.log`、`stage2-final-profile-ai.log`、`stage2-final-measure.log`、`stage2-final-lifecycle.log`、`stage2-held-exit-green.log`、`stage2-memory-green.log`、`stage2-native-settings-lazy.log`、`stage2-provider-no-device.log`、`stage2-baseline-provider-ui.log` 及各 regression 日志。

WPF 运行预览已重新构建到 `work/performance/preview/桌面伙伴.exe`，版本仍为 `0.11.2-dev.2`（文件版本 `0.11.2.0`）。SHA256：`CF9A5E355FB3D1E1C2969A6C9A23E844E451A3158F72D8D82B49444C3623E92C`。

## 后续顺序与取舍

- 优先继续文字编辑器内部首次初始化的分段测量。当前 347ms 最大样本说明还不能把“首次 AI 完全流畅”勾为完成。
- 后台字段补丁/统一提交协议、动画异步准备、历史增量与轮询优化继续按原计划由热点证据进入；本批没有为凑代码删除量修改它们。
- 采用当前状态的 UI 合并保存，代价是最后一次 durable write 仍同步；后续后台化必须覆盖全部显式写入、维护与退出顺序。
- 无设备模式保留配置覆盖，代价是真实音频/原生桌面接受性未验证；独立原基线对照和严格默认测试保留供复验。
- 审查没有其他遗留 minor findings。分支、工作树和 ledger 保留用于继续实施，未合并、推送或公开发布。
