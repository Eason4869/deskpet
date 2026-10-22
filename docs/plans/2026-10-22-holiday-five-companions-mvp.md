# 五角色节日彩蛋轻量版开发计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. 按复选框逐项执行。本轮仅形成计划，不实施、不制作素材、不构建、不提交、不切换分支。

**Goal:** 基于当前 dev 的五位伙伴，先交付万圣节短彩蛋；保留离线节日识别、农历边界和每日去重，将全天换装与衣橱延后。

**Architecture:** 公共日期、调度、记录和短动画时钟与 UI 无关，由 Windows WPF 和 Avalonia 桌面端分别接入。每位角色仅制作一段含节日服饰的完整人物动作，演出结束恢复当前正常状态，不替换伙伴身份或角色包。

**Tech Stack:** 当前仓库已有的 C#、Windows WPF/.NET Framework 4.8、Avalonia/.NET 10、ChineseLunisolarCalendar、JSON、PowerShell 和现有 Python/Pillow 工具链。公共代码沿用旧 C# 编译器可接受的语法；不新增运行时依赖。

**Spec:** 本文第 1–4 节为新方案设计依据。保留本聊天已确定的节日日期与农历要求；采用上轮提出的低成本方向作为本次推荐方案。2026-10-16 完整换装计划留作长期参考，不再作为轻量首版实施清单。尚无代码实施授权。

## Global Constraints

- 本轮只新增本计划文档；dev 正在其他开发中，不 checkout/reset/stash/pull，不更改代码、配置、图片和旧计划。
- 阅读基线：本地 dev 提交 `59be54e2d13d4bf79e1e402252a2952c70e2971a`，版本 `0.10.1-dev.1`。本地 `origin/dev` 跟踪引用当时相同；本轮未 fetch，不把跟踪引用当作实时远端查询。
- 后续实施前重新读取 dev 最新提交与未提交改动，本计划不允许覆盖并行开发；接口变化时先调整对应任务再实施。
- 首发覆盖 E包、大肥鱼、马尔芙、小贝、咕嘎，共五位；先 E包单角色验证，再制作其余四位。
- 每角色一段 8 帧、4 FPS、约 2 秒、非循环彩蛋；共 5 张动作 PNG、40 个有效画面。正常刷新时约 2 秒，UI 阻塞时允许延长，避免跳过姿态。
- 服饰仅在彩蛋中出现；不制作节日待机、跑步、睡眠、摸头、双套互动和 16 方向视线，不做全天换装、衣橱、永久收藏或新的节日头像。
- 保留 15 个活动规则：春节（含除夕）15 天、国庆 7 天、圣诞 2 天，其余仅当天。
- 使用系统本地民用日期，运行时离线；不通过 AI 或网络 API 判断是否节日。
- 同一伙伴同一本地日期最多一次自动演出；手动重播只在活动期开放，不限次数。不补播错过日期。
- 伙伴 ID、人格、聊天、工作会话、队列、音色、原有角色包和双套动作不得被彩蛋修改。
- WPF 和 Avalonia 共用日期／去重规则与动作图；本地逻辑或 Headless 测试通过不能代替 macOS 真机验证。

## Review Focus

1. 角色来源：保留旧个人 ID 的随附伙伴仍应正确匹配；导入同名／同 ID 的自定义包不能误借内置人物画面。
2. 农历边界：闰月及其后月份编号、腊月二十九／三十、春节从除夕延续至正月十四均需独立对照。
3. 动画优先级：其他工作会话、Agent 审批或恢复状态、Live、录音等不能被节日动画抢占。
4. 去重时机：候选查询不能消耗机会；首帧提交渲染后才记录；悬停和计时触发共用每日额度。
5. 渲染与恢复：透明命中使用当前彩蛋帧；取消后恢复此刻正确业务状态，不恢复播放前的过期画面。

## 1. dev 实际结构与五位角色

仓库根目录：`E:\workspace\codex\deskpet`。下文路径相对该目录。

| 角色 | 稳定素材身份 | 当前资源形式 | 当前专属动作 | 万圣节设计保留点 |
| --- | --- | --- | --- | --- |
| E包 | `e-bao` | 旧基础图集＋互动图＋追加／重绘动作 | 傲娇扭头、比心鼓励 | 白发、蓝眼、少年比例、略傲娇神态 |
| 大肥鱼 | `dafeiyu` | 旧基础图集＋互动图＋追加／重绘动作 | 摇尾欢呼、泡泡环游 | 蓝发蓝眼、耳部特征、鲸尾、原裙装轮廓 |
| 马尔芙 | `marfu` | 旧基础图集＋互动图＋追加／重绘动作 | 挥杖流光、荧光咒语 | 金发绿眼、魔杖、黑绿魔法服装 |
| 小贝 | `xiaobei` | 随附 v2 分动作包＋追加／重绘动作 | 太极光环、全息键盘 | 黑银发、机械猫耳、青色灯效、机能衣服 |
| 咕嘎 | `gugugaga` | 随附完整 v2 包，扩展动作与 9 类第二套动作由 manifest 声明 | 咕嘎摇摇、转圈摆鳍 | 黑短发、灰蓝眼、机械发夹、企鹅兜帽、白肚皮、黄脚与银色领扣 |

当前源码证据：

- `src/SkinCatalog.cs` 注册前三位；`assets/bundled-skins/catalog.json` 注册小贝和咕嘎。随附目录本身只有两项，不代表总共只有两位伙伴。
- `src/AdditionalInteractions.cs` 已按 manifest 和来源解析扩展／双套动作，并限制动作缓存。不能再按 10 月 16 日的固定附加动作实现编写新计划。
- `src/HoverActions.cs` 已有 `IHolidayHoverActionProvider.GetAction(SkinDefinition, DateTime)`；`src/PetWindow.cs` 已查询它，但没有默认节日提供者和节日素材。
- `docs/character-packs.md` 与 `src/CompanionPackStorage.cs` 已支持同 ID 更新、旧身份保留及隐藏角色。素材身份与个人会话身份不能混用。
- WPF 已拆分 `PetRuntimeAnimation.cs`、`PetManualRun.cs` 等 partial 文件；陪伴方式设置实际在 `src/SettingsPage.cs`，不是旧计划中的大型 `SettingsWindow.cs` 构造区。
- Avalonia 使用 `desktop/DeskPet.Desktop/PetWindow.cs`、`SpriteAssets.cs`、`InteractionPlayback.cs`，已有独立动画与透明命中路径；没有 WPF 的节日悬停接口，不能只接 WPF 后声称双端支持。

## 2. 推荐首版行为与成本边界

### 2.1 用户可见行为

1. 唯一新增设置“节日彩蛋”，默认开启；旧配置缺字段时同样开启，显式关闭必须保留。
2. 活动当天首次累计 30 秒符合条件的空闲时间后自动播放一段彩蛋；同一伙伴同一天最多一次。阻塞时停止累计，日期变化后重置，不补播昨日内容。
3. 活动期内右键出现“节日彩蛋 · 动作名”，可手动重播；原菜单与轮盘的技能数量、布局不做重构。
4. WPF 复用已有悬停扩展口：当日自动机会尚未使用时，可把节日动作加入候选。普通候选被选中不消费节日机会，节日候选实际播放后和 30 秒自动播放共享同一条记录。
5. Avalonia 首版提供同样的计时触发和右键入口；保留现有悬停摸摸与四格轮盘，不为追求 UI 一致重写它们。
6. 演出期间显示节日服饰和道具，约 2 秒后恢复正常人物；用户点击、拖动、聊天等可立即取消。默认不显示新的节日气泡、不朗读、不请求 AI、不写聊天历史。
7. 全部自动与悬停行为遵守免打扰；手动播放需要处于可互动状态，也不绕过关闭开关、暂停、隐藏、引导或忙碌限制。设置关闭立即取消尚未完成的彩蛋。

### 2.2 继续保留但延后的功能

| 长期能力 | 首版取舍 |
| --- | --- |
| 全天节日服饰 | 延后，不补齐全动作、全方向服装 |
| 原装版与节日装版彩蛋各一套 | 首版每角色仅一套：演出中短暂穿节日装 |
| 永久收藏、非节日手动使用 | 延后，活动期结束入口消失 |
| 节日头像、衣橱预览卡、服饰设置页 | 延后，右键入口直接使用现有 UI 风格 |
| 用户节日 ZIP／角色包扩展字段 | 延后，首版只用随应用发布的静态资源 |
| 各种节日都制作四／五角色全套素材 | 日历先覆盖规则，素材按节日逐批制作；首发仅万圣节 |

新计划完整五角色首发只需 **40 个动画画面**；旧计划仅四角色完整换装已需 663 个交付画面，工作量不可按“多加一个角色”线性照搬。新计划另有代码、规则和 QA 成本，素材缩减不等于总开发时间减少相同比例。

## 3. 活动清单与日期引擎

| ID | 活动 | 默认有效期 |
| --- | --- | --- |
| new-year | 元旦 | 公历 1 月 1 日 |
| laba | 腊八 | 非闰月腊月初八 |
| spring-festival | 春节，含除夕 | 非闰正月初一前一日至正月十四结束，共 15 天；同一套彩蛋 |
| lantern | 元宵 | 非闰月正月十五 |
| qingming | 清明 | 当年清明节气所在日期 |
| labour | 劳动节 | 公历 5 月 1 日 |
| children | 儿童节 | 公历 6 月 1 日 |
| dragon-boat | 端午 | 非闰月五月初五 |
| qixi | 七夕 | 非闰月七月初七 |
| mid-autumn | 中秋 | 非闰月八月十五 |
| national-day | 国庆 | 公历 10 月 1–7 日，共 7 天 |
| double-ninth | 重阳 | 非闰月九月初九 |
| halloween | 万圣节 | 公历 10 月 31 日 |
| winter-solstice | 冬至 | 当年冬至节气所在日期 |
| christmas | 圣诞，含平安夜 | 公历 12 月 24–25 日，共 2 天；同一套彩蛋 |

### 3.1 农历正确性

- `ChineseLunisolarCalendar.GetYear(date)` 得到农历年，不能用公历 `date.Year` 替代。转换前先检查该 API 的实际支持范围。
- 取得原始月编号 `rawMonth`、闰月编号 `leapMonth`。若有闰月且 `rawMonth >= leapMonth`，普通月序为 `rawMonth - 1`；`rawMonth == leapMonth` 另标记为闰月。其他月份保持原编号。
- 普通农历节日只匹配非闰月，不能在闰五月／闰七月／闰八月重复触发端午／七夕／中秋。闰月之后的正常月份也必须命中正确节日。
- 春节区间定义为 `[正月初一的公历日期 - 1 天, 正月十五的公历日期)`。除夕不写死为三十，腊月二十九和三十结束的年份都覆盖；正月十五仅属于元宵活动。
- 除夕时所属农历年仍是旧年，应检查相邻候选农历年的春节区间；活动实例键使用 `holidayId:startDate(yyyyMMdd)`，避免公历／农历年份混淆。
- 越界仅跳过农历规则，公历节日照常；不因一条规则失败中断桌宠。

### 3.2 节气、校验与刷新

- 清明、冬至不能套固定公历日或农历月日。首版内置 2020–2035 年的这两项节气日期，来源为香港天文台官方表；超过范围跳过节气活动并记录一次诊断，普通公历／农历判断继续工作。
- 正确性对照使用[香港天文台公农历表](https://www.hko.gov.hk/tc/gts/time/conversion.htm)和 API 行为文档。实施时准备独立官方夹具，覆盖普通年、闰月前后、春节二十九／三十、跨公历年、2033 年复杂闰月及范围边界；不能用待测转换函数自产预期值。
- 夹具保存来源 URL、获取日期和文件哈希；确认官方表与运行库存在差异时，加入带来源的例外表，不用猜测公式修正。
- 启动检查一次，运行中每 60 秒检查本地日期／时区偏移变化；恢复显示、休眠恢复、系统时间变化后尽快刷新。平台没有对应事件时，60 秒探测作为兜底，不能把“事件已绑定”当成真机验证。
- 日期来自系统本地时间；动画与 30 秒空闲使用单调时间，修改时间不会造成动画快进。跨日重新判断，不启动额外常驻线程。
- 活动全部使用 `[StartDate, EndExclusive)`；日历命中但没有 Ready 素材时不出现空菜单或错误提示。

### 3.3 重叠与每日记录

- 自动选择已发布且当前角色有图的活动。排序：春节、元宵、中秋、端午、七夕、重阳、腊八、清明、冬至、国庆、圣诞、万圣、元旦、劳动、儿童。
- 同一天重叠活动最多自动演出一个；其他有素材的活动仍可从右键手动选。不能高优先活动播完后接着自动播放低优先活动。
- 去重键为 `personaId + localDate(yyyyMMdd)`；值记录活动实例。保留旧个人 ID 的角色继续沿用其个人身份，不能改名为 canonical ID 迁移聊天。
- 计时触发、悬停触发、当天手动播放任一节日动作共享该记录；候选查询、加载资源、准备播放均不提前写已播放。
- 首帧真正进入 UI 渲染流程后消费；此前失败／取消不消费。首帧之后被用户打断不自动重试打扰。
- 状态存入独立 `data/holiday-state.json`，仅含 schema version 和每日记录，不存衣橱／解锁。采用临时文件＋原子替换；文件上限 1 MiB，最多 4096 条记录。损坏保留原文件并隔离，不能影响已有主设置或人物资料。
- 写入失败保留进程内去重并只提示一次。纯本地功能不做防改时间／防删文件的反作弊，也不保证无限历史回拨去重。

## 4. 五角色万圣素材与分镜

运行资源目录：`assets/holidays/halloween/`。每张为透明 PNG，**8 列 × 1 行，3840×520；单帧 480×520，8 帧、4 FPS、非循环**。

| 文件 | 动作名 | 节日服饰／道具 | 8 帧分镜 |
| --- | --- | --- | --- |
| `e-bao.png` | 傲娇讨糖 | 黑红短披风、蝙蝠领结、南瓜糖袋；保留白发蓝眼和少年体型 | 1–2 抱臂装酷；3–4 偷偷递糖袋；5–6 收糖得意；7–8 收势 |
| `dafeiyu.png` | 南瓜泡泡糖 | 深蓝橙色短披肩、小南瓜饰件、糖果泡泡；不遮鲸尾 | 1–2 双手托起；3–4 形成糖果泡泡；5–6 递出泡泡；7–8 尾巴轻摆收势 |
| `marfu.png` | 南瓜灯咒 | 小女巫帽、橙色披风内衬、南瓜灯，保留黑绿魔法服和魔杖 | 1–2 举杖；3–4 点亮南瓜灯；5–6 灯绕半圈；7–8 光效消散 |
| `xiaobei.png` | 全息讨糖 | 黑橙短斗篷、青橙全息南瓜，保留机械猫耳与机能轮廓 | 1–2 猫耳亮起；3–4 展开全息南瓜；5–6 接住像素糖果；7–8 界面消散 |
| `gugugaga.png` | 咕嘎南瓜糖 | 在原企鹅服上增加南瓜小披肩／糖袋，保留企鹅兜帽、白肚皮、黄脚和银色领扣；不改成普通人形女巫 | 1–2 慢半拍歪头；3–4 双鳍捧糖袋；5–6 收糖开心摇摆；7–8 抱袋收势 |

共同要求：人物和道具完整不裁切；比例、脚底线与各自原 idle 对齐；保持每位自身画风，尤其不把小贝、咕嘎强行绘成前三位的比例。第 0 帧与原 idle 姿态接近，第 7 帧收回道具，减少开始／结束硬切。允许演出时切换服饰，不宣称有换衣过程动画。

**必需成品：5 张动作图＋`assets/holidays/catalog.json` 1 份目录。** 目录只声明已完成的万圣 5 项：holiday、character_id、action_name、file、columns=8、frames=8、fps=4、loop=false、content_version。日历 15 项规则不等于目录中已有 15 项素材。

不新增绘制预览或头像。菜单如需图标，从同一动作图裁第 0 帧生成缓存；接触表／GIF／深浅背景图是 QA 派生物，不计作额外绘制资产，保存在 work 中。

资源校验：目录 JSON ≤64 KiB；单 PNG ≤25 MiB；固定总尺寸；每一帧有可见人物与透明背景；不允许空白、复制静帧凑数、混帧或 AI 光流。首版只允许目录内指定资源相对路径，不接受外部地址或脚本。

每条动作解码像素＋透明掩码约 9.5 MiB；仅加载当前角色的一条节日图，切角色或内容版本变化清缓存。不得同时把五条完整解码常驻。实际内存还包含 WPF/Avalonia/GPU 开销，后续以测量为准。

## 5. 计划中的代码文件与接口

### 5.1 新增文件：四个公共单元、两个平台适配

| 文件 | 职责与接口 |
| --- | --- |
| `src/HolidayCalendar.cs` | 15 活动、农历归一化、春节区间和节气表。`IList<HolidayOccurrence> GetActive(DateTime localDate)`；Occurrence 含 Id、InstanceId、StartDate、EndExclusive、Priority |
| `src/HolidayRuntime.cs` | 内容目录、每日资格、30 秒累计和 trigger 协调，无 UI。`IList<HolidayAction> GetAvailableActions(string characterId, DateTime localDate)`；`bool CanOffer(HolidayContext context, HolidayTrigger trigger)`；`HolidayPlaybackRequest TryPrepare(HolidayContext context, HolidayTrigger trigger)`；`MarkPresented(HolidayPlaybackRequest request)` |
| `src/HolidayStateStore.cs` | 独立每日记录，`HolidayState Load()`、`void Save(HolidayState state)`；原子写入，异常返回调用层处理 |
| `src/HolidayPlayback.cs` | 只管理短片时钟。`Start(HolidayPlaybackRequest request)`、`Advance(double deltaSeconds)`、`Cancel()`；属性 Active、Frame、Request。最多推进一帧，非循环结束自动释放占有 |
| `src/PetHoliday.cs` | WPF PetWindow 的新 partial；资源解码成已有 MotionClip、悬停 provider、菜单动作、业务门控、首帧通知与清理，不另建定时器 |
| `desktop/DeskPet.Desktop/PetHoliday.cs` | Avalonia PetWindow 的新 partial；资源转 ClipSpec，使用已有 SpriteView 绘制及命中，共用 HolidayPlayback 时钟与 Runtime，首帧通知、取消与释放 |

共享类型：`HolidayContext` 含 PersonaId、BuiltinCharacterId、本地日期、单调秒数、Enabled、Allowed，以及手动指定活动时使用的 RequestedHolidayId；`HolidayTrigger` 为 Timer/Hover/Manual；Request 含实例、个人身份、稳定素材身份、内容版本和帧参数。输入 Allowed 是平台计算后的结论，不能由公共模块猜测 UI 状态。

GetAvailableActions 返回按优先级排序的就绪活动，供重叠日菜单列出全部选项；CanOffer 只读，不累计计时、不写记录。TryPrepare 返回 null 表示不可播。手动与悬停可立即播放，Timer 需累计 30 秒；手动指定活动必须属于该日该角色的有效列表，Timer/Hover 只取最高优先项。准备后再次检查日期、角色、开关、资源版本和 Allowed；变化则取消 Request，不能把旧请求带到新角色上。

### 5.2 修改既有文件：按当前 dev 接入

| 文件 | 最小必要改动 |
| --- | --- |
| `src/SkinCatalog.cs`、`src/BundledSkinCache.cs` | 增加仅内存的可信 `BuiltinCharacterId`；前三位随附注册时赋值，小贝／咕嘎由实际 bundle 注册 ID 赋值。ReconcileLegacy 重建对象时复制该来源标记；导入包不接受 manifest 自报可信标记 |
| `desktop/DeskPet.Desktop/SpriteAssets.cs` | PetSkin 同样保存可信 BuiltinCharacterId；随附 loader 赋值、导入路径为空。节日资源按目录解析成 ClipSpec，缓存与透明掩码继续走 SpriteAssets；不根据显示名或导入包 PackageId 猜测资格 |
| `src/PetWindow.cs` | 类内初始化适配器、低频日期探测；RenderFrame/UpdateFrame/UpdateHover 接入节日帧和占有状态；CancelInteraction、切角色、隐藏、暂停、关闭统一取消；首帧沿用 shownMotion/shownSample 记录命中数据 |
| `src/PetRuntimeAnimation.cs` | 新工作／聊天／审批／待恢复／完成反馈出现时取消彩蛋；运行时取动画状态时尊重节日占有，仅在可空闲时启动彩蛋 |
| `src/HoverActions.cs` | 使用既有 IHolidayHoverActionProvider，无需改签名；最多调整候选测试，不造第二套悬停随机系统 |
| `src/Settings.cs`、`src/SettingsPage.cs` | `HolidayEnabled=true` 的构造默认、旧 JSON 默认、Normalize/Copy；陪伴方式中加入一个开关，不修改聊天模型或主题配置 |
| `desktop/DeskPet.Desktop/PetWindow.cs` | 加 partial 声明和适配调用；在现有 Tick 中推进节日时钟，避免被普通播放、视线和散步覆盖；加入活动期右键入口，不重写四格轮盘 |
| `desktop/DeskPet.Desktop/SettingsWindow.cs` | 在现有陪伴设置加入同一开关，保存／刷新与 WPF 一致 |
| `desktop/DeskPet.Core/DeskPet.Core.csproj` | 链接四个无 UI 公共源文件；不链接 WPF 适配器 |
| `desktop/DeskPet.Desktop/DeskPet.Desktop.csproj` | 将 holidays 的已就绪 JSON／PNG 编入 AvaloniaResource；不把 WPF partial 编入本项目 |
| `Build.ps1` | 新测试 Target 和节日资源嵌入；保持 UpdateApplyTests 不嵌完整资源的轻量夹具保护 |
| `tools/validate_holiday_assets.py` | 新增，检查目录、5 个角色、尺寸、帧透明度与非空，并输出 QA 清单 |
| `assets/holidays/calendar/solar-terms.json` | 新增 2020–2035 年清明、冬至离线表及来源元数据；测试用独立样本不与待测生成逻辑共用 |
| `Package.py`、`tools/runtime_package.py`、`tools/mac_package.py` | 先验证现有内嵌机制能否自动覆盖；只有检查证明确有缺口时再修改。禁止新建顶层 pets 目录或改更新包限制 |

首版不改角色 `pet.json`，不把 `halloween` 塞入仅允许固定键的 `extra_animations/alternate_animations`；不修改原 17 个标准动作契约、InteractionAnimation 索引或咕嘎双套动作定义。专用短片不参与双套随机，不影响提起→落地、投喂→开心的选版传递。

### 5.3 业务与渲染优先级

优先顺序：用户操作／业务忙碌 > 当前既有互动、睡眠和位移 > 节日彩蛋 > 普通空闲。自动门控至少检查：全部相关工作会话与队列、Agent 待审批／执行恢复、完成反馈、Live、听写、拖动／下落、手动跑动／散步、睡眠、暂停、免打扰、隐藏／全屏、锁屏、引导、菜单／设置／聊天窗口、更新倒计时。不能仅检查当前可见聊天 Session。

WPF 已有 `GreetingAllowed` 可参考，但不能直接调用含 GreetingEnabled 的函数，否则用户关闭主动问候会误关节日彩蛋。Avalonia 同样拆出节日专用可播判断，不扩张系统权限。

播放开始只停止空闲计时／散步，不通过 SelectSkin 更换角色，也不为自动动作设置 `Paused=false`。Frame=0 先提交，再累计时间；delta 非法值忽略，迟滞不追赶跳帧。结束或取消后重算此刻业务状态，不能直接无条件 Play(idle) 或恢复旧聊天状态快照。

WPF 使用彩蛋 MotionClip 和 Sample 更新图像及 AlphaAt；Avalonia SpriteView 的 Clip/Frame 同时用于 Render/Hits。若 InvalidateVisual 仅安排了渲染，尚不能记作已呈现；由实际渲染路径通知 Runtime，并校验 Request 仍为当前版本。首帧后中断仍消费当日机会。

首版对用户导入／覆盖的同 ID 角色包不借用随附节日图；保留其现有全部功能，只不增加不匹配的节日入口。未来通过明确的节日包字段支持，避免把自定义伙伴突然画成内置人物。

## 6. 分任务实施与验证

以下均待执行；测试命令中的新 Target 尚未创建。本轮不运行这些命令，不据此声称测试通过。每个任务实施时先建立失败用例、确认失败、做最小实现、运行指定回归，再审查变更；只提交该任务拥有的文件。

### Task 1：公共日历与现有规则锁定

**Files:** `src/HolidayCalendar.cs`、节气离线表、`tests/HolidayCalendarTests.cs`、`tests/fixtures/holiday-calendar/`、Build.ps1 与 Core 项目的链接配置。

**Interfaces:** GetActive；所有区间为本地 Date 半开区间；不加载人物图片。

- [ ] 在新的实施工作区重新记录 dev SHA 和正在进行的改动；确认当前是否已经出现节日相关实现，避免重复开发。
- [ ] 独立官方夹具覆盖：正常月、闰月、闰月之后、腊月廿九／三十、春节跨农历年、公历一月、复杂闰月、API 上下边界。
- [ ] 写失败用例 `NationalDaySevenDates`、`ChristmasTwoDates`、`SpringFifteenDates`、`LanternExcludedFromSpring`。精确断言 10/1–7 有效而 10/8 无效，12/24–25 有效而 12/26 无效，除夕至十四同一实例而十五仅元宵。
- [ ] 写 `LeapMonthNotFestival`、`PostLeapMonthFestivalMatches`、`SolarTermsFromOfficialTable`、`LocalDateNotUtc`、`UnsupportedDateKeepsGregorian`，预期来自官方夹具。
- [ ] 先运行 HolidayCalendarTests 确认失败，再实现，最终测试退出码 0；在 net10 公共项目验证相同规则，不引入 UI 类型。

### Task 2：每日调度、状态和可信角色来源

**Files:** `src/HolidayRuntime.cs`、`src/HolidayStateStore.cs`、`src/HolidayPlayback.cs`、Settings、两端角色来源赋值、`tests/HolidayRuntimeTests.cs`。

**Interfaces:** 第 5 节三个公共模块；状态注入临时目录，DateTime/double 由调用方提供，无需真实等待或修改系统日期。

- [ ] 写 `EligibleThirtySeconds`：29.9 秒不可计时播放，累计 30 秒可以；忙碌不累计，跨日重置。
- [ ] 写 `QueryDoesNotConsume`、`FirstFrameConsumes`、`TimerAndHoverShareDailyBudget`、`ManualPlaybackSharesBudget`、`RestartKeepsDailyBudget`、`OverlapOneAutomatic`。
- [ ] 用注入的已就绪内容夹具逐日测试国庆 7 次、圣诞 2 次、春节 15 次；不能依赖生产中尚未制作的素材。
- [ ] 写 `NoReadyContentNoAction`、`MissingArtworkDoesNotConsume`、`DateChangeCancelsRequest`、`ClockRollbackNoBurst`、`StateWriteFailureDedupesInMemory`。
- [ ] 写 `LegacyPersonaIdRetained`、`BundledCanonicalSourceRetained`、`ImportedSameIdNotBuiltin`；随附咕嘎合并旧 ID 后仍能匹配素材，导入包不能自报标记。
- [ ] 写 `OldSettingsEnableHoliday`、`FalseSurvivesCopy`、`ShortClipEightFramesThenEnd`、`CancelClearsOwnership`、`LateFrameNoSkip`。
- [ ] 先失败再实现，运行 HolidayRuntimeTests、CoreTests、ProfileTests、BundledSkinTests、ImportedSkinTests；确认无人物数据迁移或清理。

### Task 3：E包素材与两端最小播放通路

**Files:** `assets/holidays/catalog.json`、`halloween/e-bao.png`、`tools/validate_holiday_assets.py`、WPF/Avalonia PetHoliday 适配、`tests/HolidayPlaybackSmoke.cs`、对应 Avalonia Headless 用例。

**Interfaces:** 以 Request 中的资源、身份、版本驱动唯一 8 帧短片，不加入普通互动名列表。

- [ ] 先用明确的测试夹具断言错尺寸、空帧、无透明背景、资源越界、缺文件时拒绝播放且不消费当日机会。
- [ ] 按第 4 节先完成 E包 8 帧。以现有真实预览作参考，不覆盖原人物图集，不制作其他四位来掩盖单角色问题。
- [ ] WPF 接入首帧、短片渲染、命中、自动取消和旧业务状态重算；Avalonia 接入同图 ClipSpec、共享时钟与 SpriteView，不替换 InteractionPlayback 的普通双套逻辑。
- [ ] 测试 `FrameZeroRenderedBeforeAdvance`、`EightRealFrames`、`DrawAndHitSameFrame`、`DragInterrupts`、`BusyStartsInterrupt`、`RestoreCurrentBusinessState`、`IdentityUnchanged`。
- [ ] 测试 WPF 的其他工作会话忙碌／审批／待恢复时不可播；暂停、隐藏、免打扰、引导、全屏／锁屏进入后正确取消。
- [ ] 运行 E包资源校验、HolidayPlaybackSmoke、PetRuntimeTests、InteractionTests、StumbleSmoke、HoverSleepSmoke；Avalonia 运行独立新增用例及现有播放回归。
- [ ] 两端 E包通路确认后，才允许启动其他角色素材制作，避免五份返工。

### Task 4：补齐四角色与最小用户入口

**Files:** 其余 4 张动作图及目录项、WPF SettingsPage 与悬停 provider／右键、Avalonia SettingsWindow 与右键、必要 UI 回归。

- [ ] 大肥鱼、马尔芙、小贝、咕嘎各完成 8 帧；逐位核对脸型、比例、耳／尾／帽／披风以及脚底线，不只是生成不同颜色糖袋。
- [ ] 五角色校验输出恰好 5 条已就绪动作、40 个有效帧；缺任一图时本地试验只开放已有角色，最终“五角色支持”交付必须全部齐备。
- [ ] 首版只加一个节日开关和右键动作。WPF provider 仅返回当日有资格且素材匹配的动作；重复查询不记账，候选未选中不记账。
- [ ] 非活动期／关闭开关／导入角色／缺素材均无节日入口；普通菜单、WPF 轮盘和 Avalonia 四格轮盘保持正常。
- [ ] 测试 `FiveCharactersDistinctArtwork`、`SwitchCharacterCancelsOldRequest`、`HiddenCharacterNotAutoRestored`、`PackageUpdateDropsStaleClip`、`HoverDoesNotReplayConsumedDay`、`ToggleOffCancelsPlayback`。
- [ ] 回归 ActionPaletteSmoke、SettingsNavigationSmoke、ImportedSkinTests、BundledSkinTests 及 Avalonia 已有菜单／角色包／双套动作测试。

### Task 5：跨日、资源与双端交付检查

**Files:** Build.ps1、Core/Desktop 项目资源配置、两端测试工程、必要的打包测试、`docs/holiday-content.md`、`docs/interaction-extension.md`、`docs/roadmap.md`。

- [ ] 使用假时钟验收万圣 10/30→10/31→11/1、国庆 9/30→10/8、圣诞 12/23→12/26、春节除夕→元宵；绝不修改 Windows 系统时间。
- [ ] 检查加载仅当前角色一条节日图；连续切换角色／重播后缓存释放，图片和 Alpha 掩码不串角色；记录内存、空闲 CPU、异常日志。
- [ ] 两种发布路径均检查资源清单与编码，保持 UpdateApplyTests 轻量夹具，不改变更新包容量限制；不将中间素材和原始生成文件打入运行包。
- [ ] Windows WPF 视觉烟测、Avalonia Headless 与 macOS 实机播放分别留结果；缺 macOS 真机时显式记未验证，不以 Windows 上 Headless 通过代替。
- [ ] 文档清楚写“活动期间短暂演出服饰”，不宣传衣橱、全天换装、永久解锁；其他节日只写日期规则已覆盖或素材待制作。
- [ ] 验证通过后提交任务拥有的文件；本计划不包含 PR、远端推送、Tag 或 Release 授权。

### 后续执行命令（本轮不运行）

```powershell
& 'E:\workspace\codex\deskpet\Build.ps1' -Target HolidayCalendarTests
& 'E:\workspace\codex\deskpet\Build.ps1' -Target HolidayRuntimeTests
& 'E:\workspace\codex\deskpet\Build.ps1' -Target HolidayPlaybackSmoke
python 'E:\workspace\codex\deskpet\tools\validate_holiday_assets.py' --holiday halloween --require-all
dotnet run --project 'E:\workspace\codex\deskpet\desktop\DeskPet.Tests\DeskPet.Tests.csproj'
dotnet run --project 'E:\workspace\codex\deskpet\desktop\DeskPet.Desktop.Tests\DeskPet.Desktop.Tests.csproj'
```

新增纯逻辑测试需显式链接到 `desktop/DeskPet.Tests/DeskPet.Tests.csproj` 并接入 `PortableTests.cs` 入口；新增 UI 用例接入 `desktop/DeskPet.Desktop.Tests/Program.cs` 或该工程调用的专用测试类，不假设新建测试文件就会被当前自定义 runner 自动执行。

## 7. 后续批次与最终验收

扩展顺序建议：万圣机制／五角色 → 圣诞（两天）→ 春节同一套（十五天）／元宵 → 端午／中秋／国庆 → 其余单日活动。日期窗口遵守第 3 节，不随素材优先级更改。每个节日默认按 5 条短片估算，服饰设计和分镜单独立项。

只有体验确认有价值后，再另立“全天服饰与衣橱”计划；届时重新核算当前五角色及双套动作的素材量，不把旧四角色计划直接改数字作为实现依据。

交付验收：五角色 40 帧齐备、15 活动规则可测、农历有独立对照、多日每日去重、平台忙碌与操作可打断、角色包来源正确、透明命中一致、所有旧人物和双套动作兼容。保存本计划不代表这些实现或测试已经完成。

本轮交付边界：仅新增本文，旧完整计划保留；未修改 dev 产品代码、素材、构建配置或 Git 引用。实施前必须以当时最新 dev 再确认接口和并行开发范围。
