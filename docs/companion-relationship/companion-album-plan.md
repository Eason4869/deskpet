# 伙伴纪念册 F3 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. This is an additional leaf under the existing companion-lifecycle ledger, not a replacement of the unfinished main plan.

**Goal:** 用角色主视觉、阶段留言和新纪念卡，让伙伴主页具有清楚的信息顺序和可收藏的图像表达。

**Architecture:** 共享已保存关系投影不变；两端添加小型静态人物画面与阶段图形展示，主页及800×480 PNG渲染复用；沿用已有冻结PNG、保存、主题及焦点恢复合同。

**Tech Stack:** 现有WPF C#5、Avalonia/.NET10、内置角色图片和原生矢量，无新依赖。

**Spec:** [companion-album-design.md](companion-album-design.md)，补充已授权主计划；技能frontend-design明确用于这次设计。

## Global Constraints

- 不修改计分、身份、存储、解锁、反馈免打扰或AI上下文规则。
- 保持PNG800×480和缩略/预览/导出同一字节；主题/locale/伙伴/同ID外观变化正确刷新。
- WPF最窄520、Avalonia390；原色头像且名字在右侧，深卡#2B3545与页面#222832区分。
- 13语言字面量camelCase资源键，历史值不漂移；当前其他本地化审计债务留M6。
- 不操作真实用户桌面、安装与资料，不做MSI、推送、合并或发布。

## Review Focus

- 图像损坏、透明留白、非官方导入及多次换装：展示自己的可见角色或安全中性回退，不串图、不保留已销毁Bitmap。
- 未初始化或读取失败：没有假阶段欢迎/假解锁；保留真实警告和恢复入口。
- 长名字/德语日期/280字台词：固定PNG及窄窗口都完整可用；全文可访问。
- 主题/locale变化与保存窗口竞争：此次导出仍等于此次捕获的PNG，下一次预览正确刷新。
- S2已修正的实际滚动、里程碑焦点/展开与同ID换装：新结构不得回归。

## Task F3：角色主视觉、阶段留言与纪念卡统一改版

**Files:**
- Modify: `src/CompanionHomePage.cs`, `src/RelationshipHomeVisual.cs`, `src/RelationshipCardRenderer.cs` 与 `desktop/DeskPet.Desktop/` 同名文件（native helper为RelationshipHomeVisual.cs）。
- Create: 两端 `CompanionPortraitVisual.cs`，仅负责有界静态人物图像、透明边界与安全回退；不要扩大PetWindow/ChatWindow。
- 不新增六阶段几何徽章帮助类。优先复用原生图像和文字布局；确需共享只读排版参数时放入现有展示层，禁止业务计算或新持久化schema。
- Tests: `tests/CompanionHomeUiTests.cs`, `desktop/DeskPet.Desktop.Tests/CompanionHomeUiChecks.cs`；仅针对图像与卡面边界需要时加小型测试文件并接入原测试入口。
- Resources: `assets/locales/*.json`；文档更新本设计及入口表中本轮新增显示说明。

**Consumes:** RelationshipHomeView/Snapshot、RelationshipContent.Welcome(stageId,pack)、RelationshipCardData.FromSnapshot、现有HomeCardCapture、预览及文件保存合同。只从已保存阶段读取留言。图像API与所有权注意见builder check。

**Produces:** 实际主页新构图、可访问的当前阶段留言、同一图像来源的肖像纪念卡PNG；不产生下游新的业务API依赖。保留现有调用兼容；若renderer增加portrait重载，原avatar重载可委托同一核心，以免破坏已有UI测试。

- [ ] 先覆盖真实风险：当前阶段与历程欢迎语对应；未初始化不伪造；长文展开/焦点保持；损坏preview与avatar回退；透明边界保留实际像素；多次换伙伴/同ID换装图像来源正确。保留现有S2回归断言，不为像素微调写镜像测试。
- [ ] 运行新增重点测试，记录真实失败，再实现小型静态图像与阶段图形帮助类。预览加载失败不得令整页或导出崩溃。
- [ ] 实现新800×480卡面：独立肖像区和边界明确的阶段/姓名/日期区，无装饰圆弧；捕获主题与本地化文字。输出六阶段浅深卡图并直接查看，验证长名/长日期图像边界及导出字节一致。
- [ ] 接入顶部人物/留言/关系布局、纪念册及历程分区，按实际容器宽度在单/双列切换；保留所有入口、已保存状态、恢复警告与S2展开/滚动/焦点合同。
- [ ] 两端相关UI测试中英/长德语，使用现有SDK与WPF自有非活动桌面；全分支测试在M6，不重复无关维护套件。真实桌面焦点SKIP继续明确。
- [ ] 交付浅深宽窄、零分/最高/错误、长留言展开、导入角色与图像回退的实际截图；截图和生成卡面元数据清单，说明headless/inactive限制。原图库不覆盖。
- [ ] 自审、独立本地提交、完整报告与限定独立审阅；审阅通过后回到M5c2 → S1 → S3 → M6，不能把本轮美化当成完整发布验收。

图形方向参考 `work/ui-review-tools/companion-album-refined.html`，它明确是布局预览与示例数据，不是功能实现或用户逐项批准。首稿装饰圆弧已被否定，不可沿用。实际实现可调整像素与交互细节来满足原生平台与可读性，但不能退回旧的小头像大空白及细小卡面文字。
