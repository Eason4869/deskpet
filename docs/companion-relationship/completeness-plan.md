# 0.11.0-dev.1 功能闭环补充实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. 逐项实现、针对性测试和独立审阅；复用当前工作树，不重做已完成阶段。

**Goal:** 让新增关系与维护功能具备可发现的入口、真实展示、明确结果和可用恢复路径，并完成现有安装格式的验证。

**Architecture:** 共享关系/维护逻辑继续作为唯一数据来源；两套界面分别复用既有导航、气泡和 PNG 渲染器。补充为小型职责文件，不扩建窗口中的业务逻辑。

**Tech Stack:** 当前 C# 5 共享层、WPF、Avalonia、既有语言资源与原子存储、Inno Setup EXE/便携 ZIP。不引入新框架或 MSI。

**Spec:** `completeness-design.md`；原规格继续见 `implementation-plan.md` 第 2–10 节。

状态：用户于 2026-10-24 明确授权补充并实施，随后确认“不做 MSI，按补充后的计划继续，确保界面入口、测试和安装包验证”。设计可实施性检查结论为 `nothing important left`；实现者常规选择由本计划记录，不再重复请求批准。

## Global Constraints

- 固定开发基线 `v0.10.5-dev.3`，目标 `0.11.0-dev.1`；不覆盖旧 tag。
- 继续原计划全部数据保留边界、保存后展示、稳定伙伴身份、去重与免打扰规则。
- 关系开关应用级，分数伙伴级；在界面说清楚范围。
- 不添加心情、故事、收藏、记忆管理或 MSI，不引入无实际功能的占位按钮。
- 新语言键使用 camelCase 点分段，`TextResources` 调用使用字面量键；13 种语言及原文不漂移。全分支旧债由 M6 完成。
- 所有破坏性测试仅使用自有夹具；真实安装测试隔离 AppId、安装互斥量和单实例/唤醒通道。
- 不把无头渲染称为 macOS 实机验收，不把局部断言通过称为整项通过，不把本地构建称为已发布。

## Review Focus

1. 阶段文案接口存在但真实用户路径没有调用：用独特内容包文案验证两端实际控件展示。
2. 异步积分刷新、伙伴切换与保存对话框同时发生：头像、阶段、日期与预览不能串用，也不能抢焦点。
3. 保存失败、未来数据或坏 outbox：保留原文件，展示有效恢复操作，不能循环提供无效重试。
4. 搜索和快捷入口：只能导航或展开目标，不能直接清空聊天、重置或卸载。
5. 安装包身份与共存：实际构建提交、版本及文件哈希一致；测试不得唤醒或删除当前用户安装。

## 继续原计划的阶段

- [x] M1–M4：关系核心、事件、主页、反馈和导出已实现及独立审阅。
- [x] M5a：缓存清理已实现及独立审阅。
- [x] M5b1：统一维护准入/排空及实际 I/O 尾任务修复已实现及独立审阅。
- [x] M5b2：历史/上下文清理与附件保留——实现及两轮修复已通过独立审阅，损坏上下文保留，正常工作事件兼容。
- [ ] M5b3：偏好重置事务、两端数据管理界面、分别确认、状态与“查看保留附件”入口。
- [ ] M5c：卸载确认、可信助手交接、隔离安装/卸载夹具及现有更新助手夹具修正。

M5b3 必须消费 `HistoryMaintenanceService.ClearAllAsync()` 的 Complete/Busy/Partial 与 Failures；该服务自己取得维护租约，不能二次嵌套。`RemovedFiles` 以文件计，不冒充对话数量。不可读队列的阻塞原因必须说明；附件目录来自 `AttachmentDirectory`，无附件时有明确空状态。保留草稿、显式项目记忆及执行安全记录。

## F1：好感度气泡样式调整

用户补充要求：好感度增加气泡加入圆角、字体略微缩小，角色名移到头像右侧，并保持 UI 风格一致。

- [ ] 两端 `RelationshipFeedbackWindow` 使用与现有欢迎气泡一致的圆角和透明外角，沿用主题背景、边框与关闭按钮样式。
- [ ] 头像和姓名在同一行，姓名位于右侧并垂直居中；收益文字从 18 调整为 16，姓名从 16 调整为 14，验证长名字及中文显示。
- [ ] 保持现有免打扰、非抢焦点、合并收益与关闭行为，检查浅色、深色和阶段提示的实际截图。

此项紧接 M5b3 审阅通过后实施，不扩展计分或通知逻辑。复用现有界面验证，无需为这些可逆样式属性添加重复实现的单元测试。

## S1：接通阶段内容的实际展示

**Files**
- 新建 `src/Relationships/RelationshipExperience.cs`：从已保存快照异步解析当前阶段、已解锁文案及可选 AI 说明，不负责计分。
- 新建 `src/PetRelationshipContent.cs` 与 `desktop/DeskPet.Desktop/PetRelationshipContent.cs` 或等价小型组合适配器：分别衔接实际互动与既有气泡。
- 修改 `src/PetWindow.cs`、`src/GreetingService.cs`、`desktop/DeskPet.Desktop/PetWindow.cs` 的真实调用点。
- 扩展现有集成/UI 测试，并新增 `tests/RelationshipExperienceTests.cs` 的共享内容与迟到结果回归；注册便携和 CI 目标。

**Interfaces**：`new RelationshipExperience(RelationshipRuntime runtime)`，`Task<RelationshipExperienceContent> ReadAsync(string companionId, RelationshipPackContent pack, string locale = null)`。结果只读属性为 `CompanionId`、`StageId`、`Welcome`、`Interaction`、`AiContext`、`HasSavedState`、`ReadFailed`。消费 host 的 Relationships、RelationshipCompanionId、RelationshipPack、既有执行成功判定及欢迎允许条件，不改变已有事件收益/去重 API。

- [ ] 写失败测试：自定义阶段互动文本必须经实际用户动作显示；自动动作不显示也不加分。
- [ ] 以已保存关系解析文案，等待后重新核对身份、序号、免打扰和生命周期；坏数据不伪造初识。
- [ ] 接入两端真实执行成功的用户互动通道，复用非抢焦点气泡，不新增强制语音或 AI 调用。复用成长反馈/免打扰开关控制新增互动提示，欢迎沿用主动问候开关，不增加关系数据 schema 或开关。
- [ ] 接入既有欢迎流程：保持节日/预览/人格和 AI 模式优先级；非 AI 阶段候选只在正常允许的欢迎槽展示，显示后记住本会话伙伴/阶段，之后沿用原欢迎。AI 阶段说明只在相应开关开启时提供，本地阶段文本用于相应回退。
- [ ] 验证包内容与通用回退、AI 上下文关闭、保存失败、快速换伙伴、免打扰、动作忙碌以及无额外分数。
- [ ] 两端实际气泡截图与集成测试通过，独立审阅后提交。

验证入口：`./Build.ps1 -Target RelationshipExperienceTests`；便携 `--relationship-experience-only`；两端对应的真实交互 UI 目标必须加入 CI。测试用熟悉阶段 80 分及唯一内容包文本，实际互动后验证 81 分和恰好一次文本展示；自动庆祝不改变分数或重复展示。缺少接口/展示时先失败，不能仅验证解析器返回字符串。

## S2：主页预览、状态解释与恢复入口

**Files**
- 修改两端 `CompanionHomePage.cs`，必要时拆出小型 `RelationshipCardPreview` / `RelationshipHomeStatus` 控件。
- 共享状态投影置于 `src/Relationships/RelationshipPresentation.cs` 或新增同目录职责文件；两端不重复推导阈值和日期。
- 复用两端 `RelationshipCardRenderer.cs` 和 `RelationshipCardFile.SaveAsync`。
- 扩展 `tests/RelationshipPresentationTests.cs`、`tests/CompanionHomeUiTests.cs` 及原生 `CompanionHomeUiChecks.cs`。

**Interfaces**：消费已保存 HomeView/里程碑和渲染器；预览与导出共享同一次捕获的卡片数据、头像和 PNG。恢复状态只读，不能通过 UI 自动覆盖未知文件。

- [ ] 写失败测试：点预览不落盘、图像含真实姓名/头像/阶段/日期，换伙伴不会串图。
- [ ] 在主页内增加可展开预览，等比适配窄屏，复用保存动作与取消/失败提示，释放图像资源。
- [ ] 在关系信息附近显示适用的暂停、日上限、最高阶段状态及全局开关范围，不增加每日任务列表。
- [ ] 前置关系读取/保存警告与恢复操作；验证重试是否真正能恢复。对必须重启的状态明确说明，复用安全重启能力；提供用户主动打开关系数据目录的入口，不删除坏文件。
- [ ] 保存简介、规则、设置、预览展开状态与合理滚动位置；后台刷新不抢焦点，不把焦点从其他窗口拉回。
- [ ] 通过真实事件刷新、读取失败、语言/主题、最大值、零状态、键盘、窄宽与两端 UI 回归；独立审阅提交。

验证入口：`./Build.ps1 -Target RelationshipPresentationTests`、`./Build.ps1 -Target CompanionHomeUiTests` 和原生 `--companion-home-ui-only`。新增断言包括：展开预览后目录无新文件、图像解码为 800×480、后台增长后预览/滚动保留、其他窗口焦点不被夺取、成长关闭和日上限 20 的状态提示正确。

## S3：新增功能的发现与导航

**Files**
- 修改 `src/SettingsPage.cs` 及其现有路由适配；若需复用规则，新增小型 `src/SettingsFeatureNavigation.cs`。
- 消费 M5b3 数据管理入口与两端既有 About/伙伴菜单；原生没有全局搜索时不重写整个设置系统。
- 扩展现有 SettingsNavigation/AppShell 测试与原生相关入口检查。

**Interfaces**：功能名/当前语言定位词映射到既有类别与子页/关联入口；页面落点可被控件名称验证，不直接调用清理/重置/卸载服务。

- [ ] 写失败测试：搜索“清理缓存/数据管理/清空聊天/恢复默认/关系设置/伙伴主页”的当前语言名称后能到达正确位置。
- [ ] 补充 WPF 类别别名与子页落点；必要的伙伴主页关联入口复用同一主窗口路由。
- [ ] 检查原生 General、About、伙伴菜单入口及附件查看动作清晰，返回/取消保持草稿与窗口所有权。
- [ ] 验证搜索和键盘操作不会触发破坏性动作，空结果及窄屏无搜索时仍有正常菜单路径。
- [ ] 两端入口清单、相关测试和截图通过；独立审阅提交。

验证入口：WPF `SettingsNavigationSmoke` / `AppShellTests` 的真实搜索与选择路径，原生相关入口检查；搜索前后对照历史和设置文件，确认只导航、不触发清空或重置。实现新增独立测试目标时同步登记 Build/便携或原生运行器与 CI。

## M6：最终门禁与现有安装包验证

- [ ] 修复已登记的缓存 null 元数据和懒日志索引边界；严格检查历史/上下文损坏、未来数据及升级回归。
- [ ] 完成所有新字符串的 13 语言、键名/字面量、基线/来源和旧键退役审计，注明生成翻译与实机/母语审核状态。
- [ ] 修正旧引导/更新助手测试夹具，执行当前正常便携全套、相关 Framework/原生 UI 和实际资源检查；失败及未执行项分别记录。
- [ ] 在提交产品与版本代码后构建 EXE/ZIP，验证 App/助手/安装器身份与哈希。生产代码后续改动必须重建。
- [ ] 独立测试安装、升级、确认取消、助手交接、卸载和数据保留；Windows 与 macOS 实际状态分列。
- [ ] 更新中英文变更记录、用户说明、`acceptance.md` 和逐功能入口验收矩阵；记录远端 CI/tag/Release 是否实际执行。

执行顺序：M5b3 审阅/修复 → F1 → M5c → S1 → S2 → S3 → M6 → 全分支独立审阅。每项以实际实现、测试和审阅结果更新状态，不因计划已写完就勾选完成。所有新增界面继续沿用现有主题、字体、按钮层级与间距。
