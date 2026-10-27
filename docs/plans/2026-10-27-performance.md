# 本地 dev.2 性能优化实施计划

> 用户已于 2026-10-27 授权更新计划并开始优化；按 executing-plans 顺序推进，不在任务间重复询问。首批是可独立验收的设置优化，其余阶段按运行证据进入。

**Goal:** 减少启动/设置/切页的无效工作和内存分配，逐步收拢刷新、资源与保存职责。

**Architecture:** 保留现有 WPF/Avalonia/共享 Core，以具体工厂延迟构造设置子页，复用控件容器，针对可见页刷新；高风险异步保存与资源变更单列。

**Tech Stack:** WPF/.NET Framework、Avalonia 12.1.4/.NET 10、Build.ps1、自有离线 UI fixtures。

**Spec:** `docs/performance/2026-10-27-design.md`。

## 全局约束

- 基线 c68b4aa，本地版本 0.11.2-dev.2，含 origin/dev 50278dd；性能分支不冒称远端 dev 已有此版本。
- 不改源工作树、版本号、发布通道；不改用户配置，不联网操作真实频道，不停业务任务。
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

- [ ] 保存原有 EmbeddedSettingsTests 基线。
- [ ] 增加 `--measure`，报告 10 个设置实例构造/首次 render callback、30 次切页和进程末尾内存快照。
- [ ] 在未改产品前跑同一工具并保存日志。不得把这些数字叫 OS 冷启动或稳定保留堆。
- [ ] 新生命周期回归确认旧实现失败于未访问页已创建。

## 首批任务 1：WPF 顶层设置懒创建

文件：`src/SettingsPage.cs`、`src/SettingsFeatureNavigation.cs`，测试 `SettingsPerformanceTests`。

接口：私有 `EnsureCategory(int category)` 统一 AI、Work、Quiet、About、IM 的首次创建；实例字段为 null 代表未访问。现有 Select/Guide/Save 公开入口保持。

- [ ] 未访问页字段为空；HasUnsavedEdits、RefreshTextProviders、RefreshState 和项目切换检查不创建页。
- [ ] AI/Quiet 在创建时挂二级导航事件，深链接先 Ensure 再 SelectSection；读取导航状态不创建。
- [ ] 复用已创建页；已有草稿/编辑和深链接测试通过；Dispose 只释放已创建对象且幂等。
- [ ] 新测试 RED→GREEN，回归 EmbeddedSettings、SettingsNavigation、SettingsFeatureNavigation、ImChannelUi、WorkSettings、语言/Guide。

## 首批任务 2：精准隐藏页刷新与 AI 容器复用

文件：`SettingsPage.cs`、`ConnectionSettingsPanel.cs`，测试扩展 `SettingsPerformanceTests`。

- [ ] 增加回归：切换 AI 子页复用同一 ScrollViewer；返回原子页保持滚动容器；隐藏 Work/Quiet 不做无关刷新。
- [ ] ShowSection 使用延迟创建且固定的 ScrollViewer 数组，跳过相同目标；原编辑器实例、校验和保存逻辑保持。
- [ ] 隐藏 Work/Quiet 激活时刷新并遵守 HasUnsavedChanges；恢复默认只更新已创建页。
- [ ] 以工作页上下文标签和未保存输入确认隐藏刷新不会偷改状态、重新进入显示最新已提交值。
- [ ] 重跑性能样本，报告改善与限制，独立提交。

## 首批任务 3：Avalonia 顶层懒创建

文件：`desktop/DeskPet.Desktop/SettingsWindow.cs`、`desktop/DeskPet.Desktop.Tests/SettingsLazyChecks.cs`、测试 Program.cs。

- [ ] 增加 `--settings-lazy` fixture：未访问页不创建；访问后复用；IM/Provider 草稿保留；恢复默认不提前构造其他页面。
- [ ] 页面工厂列表和已创建结果分开；首次选中才构造；只 Dispose 已创建编辑器。
- [ ] 尺寸和恢复默认操作遍历/替换已创建对象；不引入 WPF 类型，不改变 pin/图片释放。
- [ ] RED→GREEN，跑 headless 导航/语言/IM 与 Release 编译；未实测的系统图形后端明示。

## 首批任务 4：报告与审查

追加任务 U（用户明确追加）：先写 `CompanionPreparationUiTests` 验证旧窗口仍可最大化的 RED；提取 WPF `CompanionPreparationWindow`，替换 PetOfficialCompanions 中临时拼装，Avalonia 同步美化并设 CanMaximize=false。复用现有本地化资源，保留后台任务、重试与停止语义。检查两端最大化配置、WPF 原生 style、状态变化，渲染截图并视觉检查。与首批一起纳入最终全分支审查。

- [ ] 在同机器同数据上保存基线/改后样本，分别记录构造、首次 render callback、切页、内存快照。完整新进程/典型压力数据单列未完成。
- [ ] 确认受影响回归、git diff --check、构建源码链接与新增文件均正确。
- [ ] 独立全分支代码质量审查；修复实质问题并补能复现问题的测试。
- [ ] 更新 `docs/performance/2026-10-27-results.md`，记录完成项、指标、限制、提交和后续门槛。

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
