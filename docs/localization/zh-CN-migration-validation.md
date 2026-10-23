# 0.10.5-dev.1 语言资源重构验收

范围为 [统一计划](planning/2026-10-23-localization-and-agent-background-jobs-plan.md) S0–S7 与 [原语言计划](planning/2026-10-23-zh-CN-json-refactor-plan.md) L01–L24。开发基线为远端 dev `4944d11416fec7b494a8eb8ac3c4b8376e1100fd`，集成正式 0.10.4 `c544752c6b5c1f0913bdab52942fc701f928b296` 后继续。版本为 `0.10.5-dev.1` / 程序集 `0.10.5.0`；未开发 Agent 后台任务。

资源为 4,344 个扁平字符串；清单有 4,999 条记录，其中 4,646 条 migrate、353 条 exclude（包括有解释的已移除文字比较点），包含七个从旧菜单身份派生的显示词条。直接从原始 dev 提交扫描得到 4,937 个中文候选，未分类为 0。清单和资源词条不是一对一关系，因此不以其比值冒充迁移完成率。

本机为 Windows x64，Framework 4.8 编译器及隔离 .NET SDK 10.0.402 / Runtime 10.0.13。证据位于忽略目录 `work/localization-validation/`，共享默认报告 `portable-report-final.json`：success=true、8,200 passed、0 failures。Avalonia 默认：3,006 UI/resource/animation assertions。资源专用 Framework：4,367 项；缺资源真实宿主：2 项；UI 解耦：7 项；角色启动恢复：11 项；主题：32 项；Python 审计：12 项。所有测试使用隔离数据；未调用付费模型。

| 验收 | 状态和证据 |
| --- | --- |
| L01 | 通过：原始提交 provenance 扫描、候选分类、普通/逐字/插值/注释与转义测试。英语固定 UI 另列人工分类。 |
| L02 | 通过：UTF-8 无 BOM、扁平字符串、重复/空白 Key、类型和空值检查。 |
| L03–L05 | 通过：独立原文基线、中文/引号/路径/换行/括号、编号模板、缺参数异常、三种 Culture；审查反向展开未发现文字和参数顺序变化。 |
| L06–L07 | 通过：缺资源独立宿主、坏目录、未知 Key 按项一次诊断、10,000 次并发读取和 Lazy 单次初始化。Framework 无 TRACE 时诊断回归已修复。 |
| L08–L10 | 通过：明确可用性/默认选项/朗读结果/菜单用途与路线身份；浏览器下载优先级及后续导航重置；旧服务名/模型名/历史配置往返保留。 |
| L11–L12 | 通过：WPF/Avalonia/macOS 层使用唯一资源源文件；完整性审计无待分类中文；Tooltip/占位/错误/空状态包含在清单。 |
| L13–L16 | 通过：默认共享旧功能回归、动态参数与取消/异常语义；保留用户/历史/模型内容，模型提示词/schema/人格和身份排除逐项固定源码上下文。 |
| L17–L18 | 通过：complete gate；每个资源必须有独立基线和已审核来源，格式参数检查、精确排除上下文及未引用资源说明；4344 个资源逐项对照原文。 |
| L19 | 通过：Windows 主程序、更新器和 Core，以及 macOS ARM64/x64 应用包的实际资源 SHA256 一致，独立 Get 正确。 |
| L20 | 通过：便携包全部哈希/源码/CRC及无外置语言 JSON 检查；Windows CI 实际静默安装后主程序/更新器独立探针；macOS 双架构应用包资源和原生启动探针。 |
| L21 | 通过：WPF 九组同状态截图与 0.10.4 对照，仅版本/夹具路径差异；Avalonia 四组截图完全相同，含 390 DIP 浅深色聊天；关闭重开检查通过。 |
| L22 | 资源/共享/Avalonia及主要本地 UI 通过；两个旧 WPF smoke 在原样 0.10.4 同样失败，见下文，不能将该部分填为通过。 |
| L23 | 通过：Windows CI 和 macOS 四项 CI 全部成功，测试提交、运行链接及平台边界见最终收口。 |
| L24 | 通过：版本按用户授权调整；无新增网络/模型行为、后台任务、权限/布局或提示词修改。0.10.4 的启动静默更新和热加载保留。 |

两个既有失败：`SettingsNavigationSmoke` 的“appearance submenu switches companion directly”和 `OnboardingSmoke` 的“actual partner selection works while navigation is locked”。同一 Windows 桌面、相同命令在不可变 0.10.4 上复现；旧断言涉及 PickerKey/Id、即时选择及引导状态假设。本次不削弱断言或改变角色业务。`ProviderUiSmoke` 首轮受音频播放/时序影响失败，独立复跑 91 项通过；两次日志均保留。

代码审查范围 `c544752..4589edb`，独立审查未发现 Critical，两个 Important 已进入一次修复：CI 产物目录错误与新增资源缺少反向来源约束，均有 RED→GREEN。正常编译中的既有字段未赋值警告保留，不冒充无警告构建。

草稿 [PR #28](https://github.com/Eason4869/deskpet/pull/28) 用于开发验收，当前不合并或发布。跨平台 CI、安装器成品和最终测试提交见下方最终收口。真实麦克风、付费模型及外部语音服务不属于本次离线文案重构的验收请求。

打包检查发现并纠正三条历史清理路径的误迁移：`src/RetiredRuntimeFiles.cs` 已逐字恢复为 0.10.4，三条清单改为历史路径身份排除，并移除三个错误的 UI 资源/基线项。既有 RuntimePackageTests 的精确生成断言先失败后通过（3 tests）；ReleaseAssetTests、WindowsReleaseTests 和 MacPackagingTests 通过。源修正后的完整核心和 Avalonia 已通过，最终数值见下方收口记录。WebView2 原生 BrowserPaneTests 29 项通过。

最新本地收口：共享 8,200、Avalonia 3,006、Framework 4,367、精简 WPF 角色集成 89、WebView2 原生 29、更新应用 25 项通过。便携候选包全部清单 SHA256、完整源码逐文件、ZIP CRC、无外置语言 JSON、主程序/更新器独立读取通过。真实发布的 0.10.3 更新器与当前更新器均应用新包，备份/设置/历史/个人外观保留。

Windows CI 在应用/WPF 全量与独立资源检查通过后，发现旧 PackagingSmoke 使用 C# 标题字面量正则，无法读取资源引用。已将检查升级为从实际编译程序集读取全部引导标题，继续逐项按顺序对照打包文档；动态计算标题负例 RED→GREEN，RuntimePackageTests 4/4，完整 fresh-source PackagingSmoke（含真实 0.10.3 更新器）通过。此修复只涉及测试和开发验收工具，应用代码与已通过双架构 macOS 的 2bf5dc3 完全相同。修复后的最终远端 Windows CI 已通过，运行链接见下方。


## 最终收口（2026-10-23）

测试提交为 `3f66ac073d2dcdb0796b69ce4f372b7fcc6120e5`。

- [Windows checks 39938690319](https://github.com/Eason4869/deskpet/actions/runs/39938690319)：全部步骤成功，包含既有 WPF 回归、共享业务、Avalonia 默认/独立退出/精简资源、旧版更新兼容、安装器构建及实际安装后的资源独立读取。
- [macOS preview checks 39938690309](https://github.com/Eason4869/deskpet/actions/runs/39938690309)：四项任务全部成功，包含打包合同、五个完整角色包、ARM64 和 Intel 原生应用包、共享/UI 回归及实际内嵌资源探针。
- 本地最终便携包 `work/localization-product/DeskPet-Windows-0.10.5-dev.1.zip` 对应同一测试提交。哈希、源码逐文件、ZIP CRC、资源字节、独立 Get 和无外置语言 JSON 检查全部通过，详见 `work/localization-validation/final-package-acceptance.json`。
- 汇总证据 `work/localization-validation/final-acceptance.json` 记录 CI、测试提交、审计、成品和两个已知基线失败；原始运行元数据及任务日志保存于同目录。

S0–S7 开发与本次资源重构验收已收口。L22 的两个既有 WPF 冒烟失败仍按上文单列，未记为通过；本次不据此改变角色业务。macOS 结果来自托管原生 runner，未声称用户设备上的人工验收。后台任务未实施，开发分支/草稿 PR 未合并、未发布 0.10.5-dev.1。

最终文档提交仅更新本验收记录和实施账本；应用、资源、构建、安装器及测试代码均与上述测试提交一致。开发候选包保留已验收提交身份，不将文档提交冒充重新构建和重新运行的 CI。
