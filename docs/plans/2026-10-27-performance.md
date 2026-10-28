# 本地 dev.2 性能优化实施计划

> 用户已于 2026-10-27 授权更新计划并开始优化；按 executing-plans 顺序推进，不在任务间重复询问。首批是可独立验收的设置优化，其余阶段按运行证据进入。

**Goal:** 减少启动/设置/切页的无效工作和内存分配，逐步收拢刷新、资源与保存职责。

**Architecture:** 保留现有 WPF/Avalonia/共享 Core，以具体工厂延迟构造设置子页，复用控件容器，针对可见页刷新；高风险异步保存与资源变更单列。

**Tech Stack:** WPF/.NET Framework、Avalonia 12.1.4/.NET 10、Build.ps1、自有离线 UI fixtures。

**Spec:** `docs/performance/2026-10-27-design.md`。

## 全局约束

- 基线 c68b4aa，本地版本 0.11.2-dev.2，含 origin/dev 50278dd；性能分支不冒称远端 dev 已有此版本。
- 不改源工作树、用户配置，不联网操作真实频道，不停业务任务。初始不发布约束已由用户后续明确授权覆盖：完成后以 0.11.3-dev.1 推送 dev，精确提交 CI 全通过后打 v0.11.3-dev.1 并发布预览 Release，保持 stable latest。
- Preserve：草稿、未保存设置、导航搜索/恢复、语言、简短反馈、节日/收藏、IM 状态。
- 先补行为回归并确认 RED，再实现和运行 GREEN；计时采用报告而非易抖动单测硬阈值。
- 不用 GC.Collect、降画质/帧数、删除用户数据换取目标数字。

## 评审重点

1. 懒页空引用和深链接/Guide 所有入口：首批任务 1。
2. IM、Provider、Work 草稿与恢复默认：首批任务 1/2。
3. Avalonia 默认恢复、语言、关窗释放：首批任务 3。
4. 保存并发与维护/退出：后续阶段 C。
5. 动画迟到资源和活跃任务：后续阶段 E。

## 首批任务 0：更新基线并建立测量入口

文件：`tests/SettingsPerformanceTests.cs`、`Build.ps1`、本设计与计划；证据在忽略目录 `work/performance/`。

- [x] 保存原有 EmbeddedSettingsTests 基线。
- [x] 增加 `--measure`，报告 10 个设置实例构造/首次 render callback、30 次切页和进程末尾内存快照。
- [x] 在未改产品前跑同一工具并保存日志。不得把这些数字叫 OS 冷启动或稳定保留堆。
- [x] 新生命周期回归确认旧实现失败于未访问页已创建。

## 首批任务 1：WPF 顶层设置懒创建

文件：`src/SettingsPage.cs`、`src/SettingsFeatureNavigation.cs`，测试 `SettingsPerformanceTests`。

接口：私有 `EnsureCategory(int category)` 统一 AI、Work、Quiet、About、IM 的首次创建；实例字段为 null 代表未访问。现有 Select/Guide/Save 公开入口保持。

- [x] 未访问页字段为空；HasUnsavedEdits、RefreshTextProviders、RefreshState 和项目切换检查不创建页。
- [x] AI/Quiet 在创建时挂二级导航事件，深链接先 Ensure 再 SelectSection；读取导航状态不创建。
- [x] 复用已创建页；已有草稿/编辑和深链接测试通过；Dispose 只释放已创建对象且幂等。
- [x] 新测试 RED→GREEN，回归 EmbeddedSettings、SettingsNavigation、SettingsFeatureNavigation、ImChannelUi、WorkSettings、语言/Guide。

## 首批任务 2：精准隐藏页刷新与 AI 容器复用

文件：`SettingsPage.cs`、`ConnectionSettingsPanel.cs`，测试扩展 `SettingsPerformanceTests`。

- [x] 增加回归：切换 AI 子页复用同一 ScrollViewer；返回原子页保持滚动容器；隐藏 Work/Quiet 不做无关刷新。
- [x] ShowSection 使用延迟创建且固定的 ScrollViewer 数组，跳过相同目标；原编辑器实例、校验和保存逻辑保持。
- [x] 隐藏 Work/Quiet 激活时刷新并遵守 HasUnsavedChanges；恢复默认只更新已创建页。
- [x] 以工作页上下文标签和未保存输入确认隐藏刷新不会偷改状态、重新进入显示最新已提交值。
- [x] 重跑性能样本，报告改善与限制，独立提交。

## 首批任务 3：Avalonia 顶层懒创建

文件：`desktop/DeskPet.Desktop/SettingsWindow.cs`、`desktop/DeskPet.Desktop.Tests/SettingsLazyChecks.cs`、测试 Program.cs。

- [x] 增加 `--settings-lazy` fixture：未访问页不创建；访问后复用；IM/Provider 草稿保留；恢复默认不提前构造其他页面。
- [x] 页面工厂列表和已创建结果分开；首次选中才构造；只 Dispose 已创建编辑器。
- [x] 尺寸和恢复默认操作遍历/替换已创建对象；不引入 WPF 类型，不改变 pin/图片释放。
- [x] RED→GREEN，跑 headless 导航/语言/IM 与 Release 编译；未实测的系统图形后端明示。

## 首批任务 4：报告与审查

追加任务 U（用户明确追加）：先写 `CompanionPreparationUiTests` 验证旧窗口仍可最大化的 RED；提取 WPF `CompanionPreparationWindow`，替换 PetOfficialCompanions 中临时拼装，Avalonia 同步美化并设 CanMaximize=false。复用现有本地化资源，保留后台任务、重试与停止语义。检查两端最大化配置、WPF 原生 style、状态变化，渲染截图并视觉检查。与首批一起纳入最终全分支审查。

- [x] 在同机器同数据上保存基线/改后样本，分别记录构造、首次 render callback、切页、内存快照。完整新进程/典型压力数据单列未完成。
- [x] 确认受影响回归、git diff --check、构建源码链接与新增文件均正确。
- [x] 独立全分支代码质量审查；修复实质问题并补能复现问题的测试。
- [x] 更新 `docs/performance/2026-10-27-results.md`，记录完成项、指标、限制、提交和后续门槛。

## 后续完整路线

| 阶段 | 文件/接口方向 | 前置证据、测试与交付 |
|---|---|---|
| A 完整运行基线 | tools/performance；启动阶段埋点（按需） | 新进程 10 次；典型/压力数据温热 30 次；UI长任务、CPU/GC、内存、写盘/解码；与首批设置样本区分 |
| B AI 编辑器真正二级懒加载 | ConnectionSettingsPanel、跨端 ProviderEditor | 先审计跨编辑器 Save/ValidateGuide/语音草稿依赖；测试直接进入 speech/live/image、未访问页不脏、保存不漏字段 |
| C 普通偏好保存/ApplySettings | PreferenceChangeSet、SettingsStore、PetWindow、所有显式保存入口 | 列出字段归属，统一提交顺序；旧普通写入不能覆盖 Provider/语言/恢复默认；维护 suspend→drain→gate→reset→resume；退出保存失败取消关闭；确定性并发和失败测试先行 |
| D 启动/聊天壳 | Program、PetAppShell、ChatWindow/ChatAppShell | 根据 trace 拆首屏和非必要工作；必要时聊天视图按需创建；恢复、Guide、队列、权限请求、草稿完整回归 |
| E 动画资源 | AdditionalInteractions、SmoothMotion、SpriteAtlas、SpriteAssets | 先记录缓存命中与解码量；异步准备/共享加载/generation/lease；冻结WPF位图、Avalonia正确Dispose；无渲染同步解码、不改动作计时/命中/画质 |
| F 历史增量与索引 | ChatWindow、ConversationHistoryIndex | 证实历史重建/分组为热点后实施；追加/加载更早/重试/图片/Markdown/滚动锚点与会话隔离测试；虚拟化独立评审 |
| G 轮询/绑定与删除清单 | PetWindow/PetRuntimeAnimation、LiveTextBindings | 以变化revision减少轮询，保留睡眠/时区/节日边界；反射只在测得热点后缓存；删除需检索构建、反射、共享与兼容入口 |
| H 整体交付 | docs/performance 报告、CI/真机 | 30 次窗口重开、100 次切页、30 次角色切换和稳定采样；跨端明确实测范围；独立审查后再决定集成和版本，不自动发布 |

每阶段独立提交、可回退；有性能证据再进入复杂改造。功能正确性是硬门槛，比例目标不是已实现承诺。现有兼容链仍被调用时不删除，单纯拆 partial 不算完成解耦。

## 验证命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target SettingsPerformanceTests -TestArguments --measure
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target SettingsPerformanceTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target EmbeddedSettingsTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target SettingsNavigationSmoke
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target SettingsFeatureNavigationTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target ImChannelUiTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target WorkSettingsTests
dotnet build .\desktop\DeskPet.Desktop\DeskPet.Desktop.csproj -c Release
dotnet run --project .\desktop\DeskPet.Desktop.Tests\DeskPet.Desktop.Tests.csproj -c Release -- --settings-lazy
```

此处为应执行命令，实际结果以 results 文档和日志为准。新 fixture 参数注册后才执行。

## 第二批执行（2026-10-27，用户明确要求继续）

基线 dc39043。顺序：B AI 真正二级懒创建 → 内存/订阅生命周期核查 → C 连续普通偏好合并与缩放应用路径 → G 会话动画状态重复分配清理 → 回归、测量、独立审查。

B：ConnectionSettingsPanel 每类独立构造；未创建不脏，显式保存只提交已访问编辑器，使用当前未访问配置进行原有校验；Guide 指定哪类仅创建哪类。移除文字编辑变化触发语音列表刷新的无关订阅。先验证 text-only、Save、Live 直达、语音草稿及界面切换。

C 第一小步：普通偏好调度仅记录 dirty，不排队 PetSettings 快照，仍使用现有 Store 在 UI 串行提交当前状态；连续滑条拖动只预览，结束提交，离散输入250ms debounce。显式保存一并提交并清除 pending；维护沿用现有 deferred 保存；退出先排空，失败取消关闭。该安全分步暂保留最终一次同步写盘，避免在未收拢全部写入入口前引入过期后台快照。

G：限定 PetRuntimeAnimation 的未变化会话集合/状态枚举，按相同优先级保留 waiting/failed/running、终态、排队与reconciliation行为。无可证实热点时不大改动画资源。

第二批状态：B 两端 AI 二级懒加载完成；Provider 专用快照/无关指令可回收回归完成；C 的 WPF 普通偏好 UI 合并、几何更新和失败保护首步完成。最终一次写仍同步，Avalonia 写盘合并、全局后台写入协议、动画/历史/轮询仍未完成，不把路线图整批勾为完成。阶段 G 本批收敛为有直接回归证据的配置副本与重复刷新清理，没有无证据重写运行状态机。见 `docs/performance/2026-10-27-stage2-results.md`。


## 第三批执行（2026-10-28）

基线 618783b，继续用户批准的本地 dev.2 优化分支。先将模型编辑弹窗的字段、能力选择与订阅推迟到实际打开；保留草稿、取消回滚、Guide 和语言切换。

普通偏好使用不可变字段补丁，后台串行执行读取、合并、序列化、Flush(true)、原子替换；同一路径 Store 共享锁及提交顺序。显式保存仍提供同步成功/失败语义，较旧补丁不得覆盖更新的显式配置，语言和未知字段按现有 JSON 保留。后台失败保留 dirty 并停止自动重试。维护预约后排空已接受写入，维护中修改延后；退出关闭新请求，先异步确保持久化成功，失败保留窗口，再进行破坏性资源清理。

验收：延迟写盘时 Dispatcher 可响应；覆盖写入次序、跨 Store 语言、恢复默认、维护排空、失败重试与退出取消；模型弹窗回归；重跑同口径测量。普通偏好后台化不等于所有业务文件都异步化，显式保存偶尔等待正在提交的后台写入应如实披露。


第三批状态：WPF 模型配置弹窗懒创建完成；普通 Scale/Volume/相关位置的不可变字段补丁、同路径提交协调、维护排空、恢复默认发布保护、退出配置预保存与失败取消完成。显式保存同步语义与 Avalonia 后台写盘仍为后续任务。三个独立审查 P2 和并发恢复回归已修复，结果与有限性能改善见 `docs/performance/2026-10-28-stage3-results.md`；不将整个性能路线标记为完成。


## 第四批执行（2026-10-28，b32cea6 起点）

用户要求持续完成本轮两个目标：优先解决首次 AI 冷构造，再推进显式保存异步化，保留保存、维护、退出保护。按已批准路线连续实施，不需要重新确认设计。

诊断先行：加入 opt-in 边界计时，不记录配置内容；同口径冷构造1192ms，rail140ms、detail controls323ms、ConnectionEditor370ms、Show/refresh约220ms。折叠的连接详情与没有实际Provider时的隐藏详情均被提前构建，是明确可删除工作。

1. 合法已保存连接按展开构造字段，空Provider按实际需要构造详情；读取/脏状态/保存不强制实例化，Guide展开保持可用。先结构回归RED，再冷/温热对照。
2. 增加显式保存的异步协调入口，将序列化与I/O放到worker，UI等待真实持久化结果后更新baseline/提示；只保留需要同步契约的内部兼容入口，实际配置按钮及Guide调用链使用await。覆盖Provider、Image、Work、MCP及相关显式入口，避免只改成fire-and-forget。
3. 显式保存登记维护排空，退出停止新请求后等待在途显式保存；保持字段顺序与失败dirty/草稿，不能因旧回调清除新错误，也不能让恢复默认或退出回写旧状态。用held worker证明UI响应、重复点击、并发编辑/维护/退出与失败恢复。
4. 相关UI/维护/语言/Guide回归、同口径测量、独立审查修复、可回退提交和本地预览；不自动合并或发布。原路线的动画/历史/轮询仍按热点证据后续推进。


第四批补齐项目级显式配置保存：独立 ProjectCatalogStore scoped 字段事务，worker在目录写锁内严格读取最新目录，只合并已变更的项目配置字段；UI发布前用同路径字段fence保护正常同步目录writer，允许更晚的明确字段修改胜出；发布保留现有ProjectDefinition引用。保持原Agent绑定拒绝忙碌任务语义，不因异步化自动停止任务。单独覆盖metadata在提交前/发布前并发写、同路径多Store、失败不发布/不遗留fence、无效目录不覆盖；完成后独立审查这一新增模块。
