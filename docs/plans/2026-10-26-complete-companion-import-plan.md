# 五角色完整包导入实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 本计划由后续负责接入的开发对话执行，本轮不启动实现。

**Goal:** 五个完整角色包经本地与官方导入路径完整安装，提供 55 种外观及真实动作，并保留现有用户数据。

**Architecture:** 共享政策负责容量边界，共享库存负责全部默认/节日资源枚举；存储复用不可变安装目录与原子指针。WPF 和 Avalonia 只适配资源来源、衣橱和渲染，官方发布沿用版本/摘要协议。

**Tech Stack:** 现有 C#/.NET Framework WPF、.NET 10/Avalonia、共享 DeskPet.Core、Python 资源工具、PowerShell 构建；不增加运行时下载库或图像生成依赖。

**Spec:** `E:\workspace\deskpet\docs\superpowers\specs\2026-10-26-complete-companion-import-design.md`。执行者必须先读完整设计。

状态：计划已编写，未实施。日期：2026-10-26。

## 全局约束

- 工作范围与资源证据以设计为准；等待其他开发收尾，再以最终 dev/用户指定基线创建 `codex/complete-companion-import` 隔离工作树。不要在历史 detached holiday-outfit 上直接做集成。
- 本地 `source@594363a`、参考 `holiday-outfit@52c791c` 仅是调查快照；所有下述相对路径应在最终工作树重新确认。
- ZIP、解压、安装、已安装校验各为 536870912 字节；1024 ZIP 条目（含目录）；单 PNG 26214400，pet.json 65536，其他 JSON 1048576，本地说明文本 1048576、官方说明文本 65536 字节，保留各自兼容边界。
- 单外观图片累计 268435456 字节；默认 v2 动作总帧 1024；每角色最多 32 节日外观；本批恰好 10 节日外观。保留现有几何、透明度和帧约束。
- 五角色：e-bao / E包、dafeiyu / 大肥鱼、marfu / 马尔芙、xiaobei / 小贝、gugugaga / 咕嘎；不得改称“小北”。
- 不删动作、不减帧、不重绘美术、不新增全身摇摆、不把节日跳跃与窗口跳跃位移叠加。必须保留眨眼、摸头 petting、戳身体 poked。
- 本地资源不提升为官方身份；更新保留个人设置、聊天及关系数据；正式收藏规则不变。
- 新节日渲染持有预算 268435456 字节/窗口，包含像素与 alpha；不把它描述为整进程内存上限。
- 未具备环境或未执行的原生验证明确标为未验证；本地计划生成不授权发布。

## 重点检查的失败条件

1. 根 pet.json 导入成功但节日目录被漏复制——任务 2、3 的完整引用与重启测试。
2. 同 ID/旧版本号/新内容导致重复角色或覆盖其他来源——任务 2、3 的来源、身份与内容修订测试。
3. 取消、磁盘满、坏图与发布指针失败毁掉旧角色——任务 3 的故障注入与最终目录复验。
4. 源代次变化、连续换装导致旧图重用、已释放图像绘制或缓存增长——任务 4 的修复/切换与字节预算测试。
5. 包在 HTML 可播但原生播放漏掉起跳/落地或触摸反应——任务 5、6 的真实命中、帧顺序与视觉验收。

## 文件职责与共享接口

以下新类型为本计划定义，现有文件仅按责任修改，不整文件搬运历史版本：

- 新增 `assets/official/companion-package-policy.json`：唯一政策数据源，构建嵌入；Python 使用同一项目内文件。
- 新增 `src/CompanionPackagePolicy.cs`：`internal static class CompanionPackagePolicy`，只读 `MaximumArchiveBytes`、`MaximumExpandedBytes`、`MaximumEntries`、`MaximumPngBytes`、`MaximumManifestBytes`、`MaximumMetadataBytes`、`MaximumTextBytes`、`MaximumOfficialTextBytes`、`MaximumAppearanceBytes`、`MaximumDefaultFrames`、`MaximumHolidayOutfits`；提供 `CheckArchive(long bytes, int entries)`、`CheckExpanded(long bytes)`，越界抛 `InvalidDataException`。内嵌政策无效时失败，不读取外部覆盖。
- 新增 `src/CompanionPackageInventory.cs`：`internal sealed class CompanionPackageInventory`；静态 `Read(string root, CancellationToken cancel)`；结果只读 `PackageId`、`ContentSha256`、`AllReferences`（`IList<string>`，规范相对路径）、`ExpandedBytes`（long）、`HolidayRoot`（string，可为 null）、`HolidayCatalog`（可为 null）。默认 manifest 仍由现有前端解析；共享库存不依赖 WPF/Avalonia。无 holidays 的旧包合法。
- `CompanionPackStorage.ReadZip<T>`、`Install` 复用原有接口；增加可取消重载，旧签名转发 `CancellationToken.None`。Install 的 validate 回调必须验证整包而非仅默认皮肤。
- WPF `SkinCatalog`、Avalonia `SpriteAssets`：读取库存、安装全部引用、绑定最终根和来源。节日来源沿用 `HolidayCatalog`、`WithCharacter`、SourceKey 机制，按本地/官方来源隔离。
- `src/Holidays/HolidayStateStore.cs`、`HolidayController.cs`：节日状态 schema 迁移；`HolidayContext.StateKey` 新增稳定来源键，`Role()` 使用它读写启用、收藏、选择和活动覆盖。键仅接受已登记的 `official:{characterId}` / `custom-pack:{packageId}`，不可使用随机 SourceKey。旧调用者通过已登记官方角色映射取得兼容键。
- `OfficialCompanion*.cs` 与 `tools/build_official_companions.py`：共享政策与完整引用，保留官方版本、目录和哈希校验。
- `PetHoliday.cs`（两端）、`SpriteAssets.cs`、`InteractionAnimation.cs` 等现有播放器：按需加载、变体与触摸、位移及释放；只改缺失部分。
- 构建注册：`Build.ps1`、`desktop/DeskPet.Core/DeskPet.Core.csproj`、测试入口/项目；新共享源、政策资源必须两条构建路径均包含。

## 任务 0：确认最终基线与输入

- [ ] 在其他工作完成后，执行 `git status --short`、`git log -1 --oneline`、`git worktree list`，记录实际基线；先读该工作树 AGENTS.md。存在重叠未完成开发时等待或在其最终提交后实施。
- [ ] 使用上述设计列出的文件清单定位最终对应实现，记录差异；若已有统一政策/完整导入器，扩展现有实现，避免平行重复。
- [ ] 从 `E:\data\HDskin\delivery-manifest.json` 验证五 ZIP、包内文件与交接元数据；记录大小、条目、角色、10 节日 ID、各动作/变体数量。使用脚本读取清单实际格式，不假定 JSON 结构。
- [ ] 在独立工作树新增 `tests/CompleteCompanionFixtureTests.py`，接受 `--artifacts E:\data\HDskin`，只读资源；断言五角色/55 外观、引用存在、源摘要一致、小贝命名。记录初始默认图片摘要，供后续包内比较。

**完成证据：** 基线提交、资源检查 JSON、没有改动其他开发的脏文件。

## 任务 1：统一政策并覆盖全部限制点

**Files:** 新政策 JSON/读取器；修改 `CompanionPackStorage.cs`、`OfficialCompanionModels.cs`、`OfficialCompanionFeed.cs`、`OfficialCompanionDownloads.cs`、`OfficialCompanionPackageValidation.cs`、`OfficialCompanionStorage.cs`、`IllustratedPack.cs`、Python 打包器和构建注册。新增 `tests/CompanionPackagePolicyTests.cs`，接入现有测试入口。

**Consumes/Produces:** 提供上节 `CompanionPackagePolicy` 接口；不改变其他业务模块上限。

- [ ] 先写边界测试：536870912/536870913 字节、1024/1025 条目、26214400/+1 PNG、65536/+1 manifest、1048576/+1 JSON、逐外观 268435456/+1。大值优先测纯政策函数，避免为单测分配 512 MiB 内存。
- [ ] 增加 Python/应用政策一致性测试、无效或被 ZIP 替换的政策不能提高上限；保留旧默认/v1/v2 包兼容测试。
- [ ] 运行上述测试，确认旧实现不能接受合法大包/条目或缺少共享政策而失败；再实现政策和所有调用点，包含下载实际字节、解压累计、安装累计、收据重开检查。
- [ ] ZIP 检查实际文件长度、文件目录整体安全性和实际解压写入长度；使用 checked long。目录导入也检查单文件和总量。
- [ ] 更新两套构建资源注册；测试通过后提交此独立变更。扫描剩余 256 MiB/128/512 条目常量，保留属于单外观/其他业务的限制并注明用途，不能全局替换数字。

**完成证据：** 两端使用同一政策，512 MiB 内包能越过容量检查，越界仍拒绝；Python 打包与运行时一致。

## 任务 2：完整库存与身份/来源协议

**Files:** 新 `CompanionPackageInventory.cs`、`tests/CompleteCompanionInventoryTests.cs`；复用 `Holidays/HolidayCatalog.cs`、`OfficialCompanionIdentity.cs` 的既有规则，必要时增补接口。

**Consumes:** 政策、既有 JSON/安全路径/节日目录解析。**Produces:** 上节库存 API；确定性内容摘要（按规范路径序排序，摘要输入包含路径、长度和各文件 SHA256，排除应用生成收据）。

- [ ] 写测试并先见红：默认旧包、完整 10 节日包、缺一张图/缺 outfit.json、重复节日 ID、角色身份不一致、catalog 与定义版本不一致、未引用图片、跨根路径、大小写碰撞、链接/重解析点、设备路径、说明文件超限。无 catalog 却残留 holidays 资源不能静默忽略。
- [ ] 实现库存：收集默认动作/额外/alternate/gaze/avatar/preview、companions.json（存在时）、catalog、各 outfit 定义/动作/变体/gaze/easter_egg/preview；同规范路径去重。
- [ ] 按外观分别计量，解析 32 节日上限；这批 fixture 另断言恰好 10 套和 27/28 clip 结构。1024 默认帧上限不要跨皮肤累加。
- [ ] 写身份回归：本地同 ID 仅更新同来源；自定义和官方同名并存不串库；同 ID 同摘要不重复安装；同 ID 同旧版号但不同摘要可更新本地修订，不因此改写线上官方包。
- [ ] 写服饰状态回归并实现 schema 迁移：同名本地/官方分别换装、开关、解锁、活动手动覆盖，重启后互不影响。唯一可证明的旧来源保留其原设置；歧义旧 CharacterId 记录仅迁移到官方键，本地使用默认状态。迁移前备份，旧记录不丢失，重复启动幂等；资源修订不改变持久键。
- [ ] 验证通过，提交库存与来源变更。

**完成证据：** 两入口和后续安装器拿到相同 AllReferences；故意漏节日图的包被整体拒绝。

## 任务 3：完整安装事务与两端衣橱登记

**Files:** `CompanionPackStorage.cs`、`SkinCatalog.cs`、`desktop/DeskPet.Desktop/SpriteAssets.cs`、`PetOfficialCompanions.cs`、`desktop/DeskPet.Desktop/OfficialCompanionSource.cs`、相应两端节日来源绑定文件；新增 `tests/CompleteCompanionImportTests.cs` 与 `desktop/DeskPet.Desktop.Tests/CompleteCompanionImportChecks.cs`。

**Consumes:** 库存、政策、原生验证器。**Produces:** 最终安装目录中完整引用；正确来源的衣橱目录；稳定身份和个人数据。

- [ ] 先写回归：ZIP 和 pet.json 目录输入均安装默认 +10 节日、最终根引用可重开、重启不丢失、重复导入不重复角色；记录旧代码漏装 holidays 的失败。
- [ ] 让两端本地导入使用 AllReferences。安装回调逐图做当前平台解码和透明内容验证，并从最终目录完整复验；复用官方原生验证器中可共享逻辑，不能只查 PNG 头。
- [ ] 在工作线程执行库存/哈希/解码/复制，进度阶段为“检查、解压、验证、安装、完成”；回 UI 线程一次性登记目录与渲染来源。支持取消，不占用 UI 线程遍历整包。
- [ ] 扩展取消重载；注入磁盘不足、权限/文件锁、损坏图、取消和指针提交失败，断言旧 current 指针、目录、衣橱选择和个人设置保持有效。暂存清理限定本操作创建的目录。
- [ ] 测试指针提交后立即终止并重启：以有效最终指针恢复；目录刷新失败可重试，不能把已提交安装误判为可删暂存。
- [ ] 明确提交点前后的取消语义及身份映射恢复顺序：验证前不改现有角色设置；提交前失败保持旧指针，提交后取消不回收有效新版本。覆盖导入时尚未完成 UI 刷新的重启场景。
- [ ] 安装空间预估按设计统计；失败信息显示实际阶段、大小与上限，不能所有错误都变成“网络失败”。接入当前本地化体系与翻译资源，不加语言切换重启提示。
- [ ] 提交并保存 ZIP/目录两入口各五角色的安装与重启证据。

**完成证据：** 每条入口均是 5 角色/55 外观；个人数据前后对比无变化；损坏包失败不影响旧版。

## 任务 4：按需渲染与缓存生命周期

**Files:** `src/PetHoliday.cs`、`desktop/DeskPet.Desktop/SpriteAssets.cs`、`desktop/DeskPet.Desktop/PetHoliday.cs`；新增 `tests/CompleteCompanionCacheTests.cs`、桌面对应检查。现有类能承载时不另建缓存框架。

**Consumes:** 带来源和修订的最终目录。**Produces:** 可观测缓存字节、命中/解码计数；新节日持有量不超过 256 MiB/窗口。

- [ ] 写测试：枚举衣橱不解码全部动作、同 clip 命中复用、同 ID 同版本新内容不读旧图、角色切换不串图、淘汰清理对应 mask、关窗解除持有。
- [ ] 给已有少量缓存补充按像素/alpha 实际持有的字节计量与淘汰；避免对同一共享图重复记账，同时不得漏算 detached frame。安装验证图像不得进入长期播放缓存。
- [ ] 固定正在绘制/过渡的帧，提交新画面后再释放旧 Bitmap；快速换装、窗口关闭和取消触发的晚到回调按代次丢弃，后台验证不依赖 UI 线程派发。
- [ ] 连续切换 55 外观 30 轮，记录内存/缓存/句柄，按设计的预算与后 10 轮增长目标验收；查明失败后再调预算，不能仅提高预算掩盖泄漏。
- [ ] 提交并保存性能记录，列明机器、平台、分辨率、缩放、多窗口数量和冷/热状态。

**完成证据：** 资源持有随当前使用收敛，无已释放图像绘制异常，无随全部历史皮肤持续增长。

## 任务 5：日历、变体与真实交互动作

**Files:** `src/Holidays/HolidayCalendar.cs`、`HolidayController.cs`、`HolidayPlayback.cs`、`src/PetHoliday.cs`、`src/PetRuntimeAnimation.cs`、`InteractionAnimation.cs` 及两端对应文件；扩展 `HolidayCalendarTests.cs`、`HolidayRuntimeTests.cs`、`InteractionTests.cs`、桌面交互检查。

**Consumes:** 全部 clip 定义、既有日历/收藏规则。**Produces:** 正确皮肤、动作顺序与触摸反馈；正式收藏状态不受测试影响。

- [ ] 先写日期边界回归：2/13、2/14、2/15；9/30、10/1、10/7、10/8；春节/元宵/端午/中秋/七夕跨年与闰月；清明数据覆盖/不覆盖年份、本地跨午夜、同时命中多个节日的既有优先级。复用已有黄金日期集。
- [ ] 最终基线若缺情人节则补 2/14，其他规则仅修确证缺口；国庆固定中华人民共和国 10/1–10/7。测试使用注入时钟，不修改操作系统日期。
- [ ] 先写播放回归：8 帧节日 jump 首尾/落地/恢复可见，单次动作正确结束，fps/loop/variant 原样生效；节日跳跃不叠加窗口位移，默认跳跃和拖拽落下不回归。
- [ ] 写摸头/戳身体命中测试：在 100%/150%/200% 缩放下命中当前帧头部与躯干，触发 petting/poked 不同 clip；透明区不触发；不会因每次鼠标移动无休止重置第 0 帧。
- [ ] 测试睡眠唤醒、拖拽/取消、切角色/换装、暂停/恢复期间的交互中断，遵循现有优先级；禁止恢复时闪回旧角色默认帧。
- [ ] 逐套查看待机眨眼、挥手、跳跃、打盹及两种触摸；使用网格、逐帧与正常速度共同检查。咕嘎不频繁歪头、小贝命名、红旗/月饼保持交付效果。
- [ ] 提交并保存动作报告，自动帧序证据与人工/视觉检查证据分别记录。

**完成证据：** 50 节日套装的关键动作原生可触发，默认 5 套保持旧行为，收藏及自动换装规则有效。

## 任务 6：官方资源适配、全量验证与交接

**Files:** `tools/build_official_companions.py`、`tests/OfficialCompanionAuthoringTests.py`、`tests/OfficialCompanionPackageSmoke.cs`、`tests/OfficialCompanionTests.cs`、`tests/WpfOfficialCompanionTests.cs`、`desktop/DeskPet.Desktop.Tests/OfficialCompanionUiChecks.cs`、格式文档、README/CHANGELOG 及版本配置。

**Consumes:** 本地已验证资源、统一政策、目标发行流程版本。**Produces:** 可发布候选五包/索引、验证记录；实际发布等待用户安排。

- [ ] 先增加测试：错误附件名被拒绝、SHA/大小不一致被拒绝、过低 min_app_version 不可对旧客户端宣称兼容；索引恰好五角色、每项十节日、最终 ZIP 默认图像摘要与输入一致。
- [ ] 打包工具增加明确的完整资源输入参数，默认来源不再只能从旧内嵌节日库导出；读取统一政策，拒绝缺图、额外残留文件和超限。
- [ ] 采用设计中的版本规则生成候选包 `DeskPet-Companion-{id}-{version}.zip` 和官方索引；更新必要版本元数据与摘要，不改图像。禁止为了适配而放宽 Feed 命名、来源及摘要校验。
- [ ] 用本地假 transport/fixture 模拟官方目录、下载和断网回退，完成真正官方安装路径的五包校验/原生解码/收据/重启；真实 GitHub 发布不属于该测试。
- [ ] 执行下面的验证命令，输出归档到候选验证目录；再按设计完成 Windows WPF 与 Avalonia 55 外观原生视觉和交互验收。macOS 只有真实执行后才能标为通过。
- [ ] 更新 skin-format/资源制作说明：完整包政策、目录结构、所有动作与变体、失败语义、单皮肤和完整包的不同限制。README/CHANGELOG 与实际版本保持一致。
- [ ] 完成独立代码审阅，修复阻断问题，提交代码与报告；向用户交接最终提交、测试结果、资源摘要、兼容版本、平台未测项及发布顺序。用户未要求发布时停在可审查候选，不上传或合并。

### 验证命令模板

在最终工作树运行。下列是现有仓库命令形态；任务新增测试必须先注册进对应入口。新测试目标为计划新增，不代表今天已经存在或执行过。

```powershell
python tests/CompleteCompanionFixtureTests.py --artifacts E:\data\HDskin
python tests/OfficialCompanionAuthoringTests.py
dotnet run --project desktop/DeskPet.Tests/DeskPet.Tests.csproj -- --official-companion-only
dotnet run --project desktop/DeskPet.Tests/DeskPet.Tests.csproj -- --holiday-source-only
dotnet run --project desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj
powershell -NoProfile -File .\Build.ps1 -Target ImportedSkinTests
powershell -NoProfile -File .\Build.ps1 -Target HolidayCalendarTests
powershell -NoProfile -File .\Build.ps1 -Target HolidayRuntimeTests
powershell -NoProfile -File .\Build.ps1 -Target InteractionTests
powershell -NoProfile -File .\Build.ps1 -Target WpfOfficialCompanionTests
powershell -NoProfile -File .\Build.ps1 -Target OfficialCompanionPackageSmoke -TestArguments <候选五包与新索引所在目录>
```

补充注册新 `CompanionPackagePolicyTests`、`CompleteCompanionInventoryTests`、`CompleteCompanionImportTests`、`CompleteCompanionCacheTests`，以及真实资源验收入口；通过 Build.ps1 和 portable 测试入口显式执行，不能只写文件而不接入运行。

## 交付检查表

- [ ] 本地 ZIP、目录、官方 fixture 三入口均通过；五角色、55 外观，重启可用。
- [ ] 512 MiB 和 1024 条目边界生效，25 MiB 单 PNG 与安全校验保留。
- [ ] 50 节日所有 clip 可读取，摸头/戳身体/眨眼/跳跃有原生证据，默认图像摘要一致。
- [ ] 故障回退、身份来源、个人数据保留、缓存释放和 30 轮性能记录齐全。
- [ ] 发行名、版本、兼容下限、索引与摘要一致；候选资源没有被当成已上线版本。
- [ ] 两平台验证范围如实说明；未执行项不勾选，未完成原生安装不称完整接入。

建议顺序：0 → 1 → 2 → 3 → 4 → 5 → 6。由接入对话顺序实施和验收；不在其他功能尚未完成时并行修改共享导入/角色/节日模块。

## 计划审阅记录

2026-10-26 独立上下文审阅发现“同名本地/官方角色服饰持久状态归属”未明确；现已补充设计的稳定来源键、旧状态一次性迁移规则及任务 2 的重启回归。审阅未发现其他重要设计缺口。此记录仅代表计划审阅，不代表产品代码、实际导入或平台验收已经通过。
