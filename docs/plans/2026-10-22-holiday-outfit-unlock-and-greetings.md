# 五角色节日换装、隐藏解锁与主动问候开发计划

> **2026-10-22 当前实施更新：** 用户已重新授权以最新远端 dev `4be9d476` 实施完整计划，目标版本 `0.10.3-dev.1`，不受此前 17:45 限时约束。随后要求五套服饰与原装有较大区别，已采用下文更新的完整新装设计；新增会话归档错误优先修复，并按临时分支 CI 检查点通过后继续开发。旧 [限时开发交接](../holiday-development-handoff.md) 仅记录此前基础阶段。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. 按复选框跟踪。原计划的“仅交付计划”限制已由上方当前实施授权替代。

**Goal:** 先为五位内置伙伴完成万圣节：节日零点自动换装，悬停偶遇专属动作后永久解锁服饰入口，并让主动问候自然适配节日。

**Architecture:** 日期识别、服饰目录、解锁存储和触发规则由 WPF 与 Avalonia 共用；两端各自接入动画、透明命中、设置及问候。服饰作为当前伙伴的视觉变体，不通过切换伙伴身份实现。用共用云雾遮罩完成换装，服装本体仍需覆盖实际使用的动作与视线画面。

**Tech Stack:** 现有 C#、WPF/.NET Framework 4.8、Avalonia/.NET 10、ChineseLunisolarCalendar、JSON、PowerShell、Python/Pillow。公共源码保持 Framework 构建器兼容语法，不新增运行时服务或在线日历依赖。

**Spec:** 本文第 1–5 节为本次行为规格，落实用户最新四条体验要求和先前节日时间规则。本文替代 `docs/plans/2026-10-22-holiday-five-companions-mvp.md` 的轻量首版范围；旧文保留为历史。特别废止旧文的“仅演出时穿衣、不做衣橱、右键重播、定时自动播放彩蛋、每天只播一次”等决策。

## Global Constraints

- 实施基线：以远端 `dev` 的 `4be9d476d91c7fe6e7de4500700ba63f86e27f8a`、`0.10.2-dev.3` 建立隔离工作树；第二轮 CI 通过后快进到 `02b022986b46939aeb46026473e623ad0f3979d3`，保留开发期间新增的 Git 过滤器安全修复。最终版本为 `0.10.3-dev.1`。
- 用户已授权完整开发及分批推送临时分支验证 CI。每个检查点通过后继续开发；不覆盖原工作树的未提交内容，不自动合并、打标签或发布 Release。
- 首个有完整素材的节日是万圣节；角色为 E包、大肥鱼、马尔芙、小贝、咕嘎。先 E包验证，再扩展另外四位。
- 特殊动作只能经鼠标悬停随机触发；设置、右键、轮盘、快捷键、AI 工具和普通动作自动调度均不提供重播入口。零点换装动画是视觉过渡，不算解锁彩蛋。
- 一次有效彩蛋只解锁当前角色对应的服饰；选择已解锁服饰是允许的主动操作，选择本身不会播放彩蛋或解锁其他服饰。
- 伙伴 ID、人格、音色、聊天、Agent 工作会话与队列不因换装变化。既有动作、双套互动、睡眠及视线能力继续工作。
- 日期用系统本地民用日期；动画与冷却用单调时钟。免打扰、暂停、隐藏、锁屏、工作忙碌优先于节日演出。
- 春节含除夕共一套，除夕至正月十四；国庆 10/1–10/7；圣诞 12/24–12/25；其他仅当天。
- 两端复用同一份日期与状态规则。Windows 本地或 Avalonia Headless 通过，不能冒充 macOS 真机验证。

## Review Focus

1. 旧个人 ID、同 ID 更新、自定义同名包：只让可信内置角色取得自己的服饰，不串角色，也不重建聊天身份。任务 2、5 验证。
2. 零点、跨年、休眠恢复、时间回拨：正确进入/退出活动；不每天重复变装、不补发错过节日的解锁。任务 1、3 验证。
3. 用户拖动或工作任务打断彩蛋：立即释放动画占用；未完成演出不解锁，当前工作反馈继续显示。任务 4 验证。
4. 已解锁但资源缺失、状态写入失败或设置页仍开着：不显示黑影/空列表异常，不假报永久保存，入口状态能实时更新。任务 1、2、5 验证。
5. 节日问候请求跨过午夜或切角色后返回：不显示旧节日/旧角色问候，不额外开启 AI 或语音。任务 6 验证。

---

## 1. 根据当前 dev 得出的范围判断

1. 前三位在 `src/SkinCatalog.cs` 中注册；小贝、咕嘎在 `assets/bundled-skins/catalog.json` 中注册，共五位。不能只读取随附目录的两项。
2. `src/HoverActions.cs` 已有 `IHolidayHoverActionProvider`，WPF 目前从候选中均匀随机。Avalonia 的悬停目前直接触发 `petting`，需要单独接入节日分支。
3. WPF 设置位置是 `src/SettingsPage.cs` 的“选择伙伴”区；Avalonia 是 `desktop/DeskPet.Desktop/SettingsWindow.cs` 的 `Partner()`。
4. `src/GreetingService.cs` 负责 AI 问候及本地回退，`src/GreetingSchedule.cs` 包含原有词库；不应另造一套主动说话定时器。
5. 当前服装已经画在人物帧中，没有适用于五角色的骨骼或服装锚点。持续穿着节日服饰需要整套视觉资源；简单叠加帽子、披风不能保证转身、跑步、提起时贴合。
6. 当前扩展动作存在原版/新版，咕嘎还在 manifest 中声明双套跳跃等动作。完整覆盖的素材规模明显大于旧轻量版的 40 帧。

## 2. 最终体验与默认决策

以下是本计划采用的推荐默认值，均属于设计决策，不表示代码已经实现。

### 2.1 首次发现与永久衣橱

| 场景 | 显示与行为 |
| --- | --- |
| 从未解锁、普通日期 | 设置中完全隐藏“衣柜”，伙伴下拉框保持原宽度，不留占位，不展示锁定列表 |
| 从未解锁、进入有素材的节日 | 自动临时穿上当日服饰；设置入口仍隐藏 |
| 当日悬停抽中彩蛋，完整播完 | 解锁当前角色这套服饰；保存成功后提示一次“已解锁「服饰名」，可在设置中更换” |
| 至少成功解锁过一套 | 原伙伴下拉框区域平分为等宽的“选择伙伴”和“衣柜”下拉框，入口常驻，跨日与重启保留 |
| 切到另一个尚未解锁的角色 | 衣柜仍在，仅有带当前伙伴缩略图的“默认”项；不借用其他角色服饰 |
| 普通日期主动选择已拥有服饰 | 正常换装，并可持续穿着；彩蛋仍只能通过悬停随机遇到 |

**关键区分：节日期间自动穿上是临时体验；完成悬停彩蛋才是永久拥有。** 零点换装不直接解锁，否则首次进入节日就会失去“偶遇后发现隐藏功能”的效果。

- “成功触发”在本计划中采用严格、可验证的标准：资源就绪、首帧显示、非循环彩蛋完整播放结束，且结束时仍为同一角色、同一有效活动。仅排队、抽中、加载成功或播放被打断都不解锁。
- 解锁按 `BuiltinCharacterId + OutfitId` 保存；入口发现标记为本地用户全局。角色删除/恢复、应用升级不清除收藏；删除本地应用数据不在保留保证内。
- 衣柜下拉框显示带缩略图的“默认”和当前可信角色已拥有的服饰，不列其他角色、未发布或锁定服饰。各伙伴独立保存选择，切换伙伴同步更新衣柜；两个下拉框等宽，导入/更新角色包按钮保持在右侧，沿用现有主题、边框和间距。已拥有但资源损坏的一项显示为禁用的“暂不可用”，收藏记录不删除。
- 首次解锁提示是短系统提示，不是主动问候，不触发 AI/TTS、不写聊天历史；忙碌时推迟提示，关闭提示不影响已经保存的解锁。
- 本轮不做服饰商城、衣橱图鉴页、解锁进度、成就系统、云同步或自定义服饰导入。

### 2.2 开关、选择与节日自动切换的关系

- “特殊服饰”开关按角色保存，初始为开启；首次发现前不显示开关。关闭后恢复原装，阻止该角色自动换装及服饰彩蛋；收藏和常驻入口保留。节日主动问候仍由原有问候开关控制。
- 下拉框只管理服饰，不触发专属动作。关闭时禁用下拉框但保留上次选择。
- 保存用户主动选中的 `PreferredOutfitId`；节日临时覆盖不改写这一偏好。节日结束后恢复用户原先的已解锁选择；此前未手动选择过则恢复原装。
- 活动开始时，开关开启的角色自动穿上当日服饰。用户在活动中手动换衣后，本次活动不再抢回节日装；这一覆盖决定跨重启保留。下一次新活动仍可自动换装。
- 活动中关闭开关后，之后的活动也不强制开启。重新开启时按当前活动及已保存的本次活动覆盖决定显示，避免无故清空用户选择。
- 下拉显示当前实际穿着且已解锁的服饰。若正在临时体验尚未拥有的衣服，显示“当前体验尚未解锁”的占位提示，不把它作为可选项。此提示只在区域已经发现后出现。
- 换角色不会顺带解锁：活动内切到另一个内置角色，可临时穿上其节日装，但仍需该角色自己的悬停彩蛋才能收藏。

### 2.3 零点换装：共用云雾变装

选择 **约 0.8 秒的云雾变装**。6–10 个柔边圆形/径向渐变云团聚拢，在完全遮住人物时切换服饰，再散开。五角色共用数学轨迹和时序，WPF/Avalonia 各绘制一层，省去五套旋转转身帧。

- 0–250 ms 聚雾，250–450 ms 覆盖人物并在中点换装，450–800 ms 散雾；不加声音。效果只覆盖桌宠原视口，不扩大透明窗口，也不取得焦点。
- 图层不参与命中；角色的点击命中始终来自当前实际人物帧。拖动、业务动作或用户快速换衣打断时，直接落到最新有效外观，清掉云雾，不延后抢回播放权。
- 程序运行且空闲时，本地日期越过 00:00 后的首个检查点开始换装，目标检测延迟 ≤1 秒；UI 阻塞时不承诺墙钟毫秒级准时。
- 日期探测先于暂停/隐藏的动画早退执行。锁屏、免打扰、拖动、睡眠和工作忙碌期间更新“应穿服饰”；在安全帧提交对应外观，不插入庆祝动画、不重启动作。确实无法安全提交时等当前动作结束。
- 离线/关机时不唤醒电脑；启动、恢复、系统时间/时区变化时重新判断，仍在活动中才穿上，不补演已经结束的活动。
- 活动实例键是 `holidayId:startDate(yyyyMMdd)`。春节 15 天、国庆 7 天、圣诞 2 天只在进入实例时变装一次，不在每天 00:00 重播。同一进程切回角色无需反复播放过渡；状态快照保存到本地，重启不重复初次变装仪式。
- 普通日期手动换装可复用同一云雾；关掉特殊服饰可直接恢复原装。过渡本身不解锁，且不算节日专属动作。

### 2.4 悬停概率与动作约束

推荐首版参数：**活动期间每次有效悬停，30% 抽中当日服饰彩蛋；其他时间穿着已解锁服饰时为 20%；60 秒冷却。** 这些是可调产品参数，不做用户可见的概率设置页。

- WPF 保留“进入人物有效区域后的一次悬停机会”；抽中普通分支时沿用当前普通候选。当前四个普通候选若与一个节日候选等权，节日概率是 20%；改为 30% 是小幅提高。
- Avalonia 在当前 `petting` 分支前做同样的概率判断；未命中仍摸头，不顺手改造整套普通悬停体验。
- 一次进入只抽一次；鼠标停住、像素抖动、动作结束、冷却到期都不自动重抽。离开桌宠视口后再进入才重新获得机会，沿用/补齐两端的一次进入门控。
- 冷却从成功开始播放首帧计时，被打断也占用本次冷却。按角色+服饰、单调时钟计；不因开关、切角色或反复进出设置刷新。跨进程不保证保留秒级冷却。
- 活动中只在实际穿着当前活动服饰时触发该活动彩蛋；用户手动换成别的已拥有服饰后，只走那套服饰的普通日期规则，不突然换回活动装。
- 首次解锁只接受活动期内完成的对应彩蛋。非节日穿已拥有服饰时的彩蛋不新增解锁，不再次弹解锁提示。
- 原装、开关关闭、无对应素材、点击穿透、暂停/睡眠/忙碌等情形不提供特殊候选；未命中、准备失败不进入冷却。连续动作不能让宠物卡在特殊状态。
- 彩蛋建议每角色 8 帧、4 FPS、非循环，约 2 秒；正常刷新下按时结束，UI 卡顿时不一口气跳过所有姿态。用户拖动/点击、隐藏、切角色、业务状态变化可以立即打断。
- 不采用“当日必出”或每天只播一次；也不在右键菜单、普通招牌动作按钮、问候挥手和 AI 动作里暗接节日动作。测试使用注入时钟/随机源，不新增用户可操作的节日重播入口。

### 2.5 节日主动发言

- 复用现有 `GreetingSchedule`、`GreetingService`、问候气泡和用户开关；日期只给现有问候机会增加上下文，不另起定时器、不卡零点发言、不提高发言频率。
- 首发为每角色制作 6 条万圣节本地文案，共 30 条；保留现有近期去重、字数限制、超时回退和安静模式。第一次允许的节日问候优先节日词库，随后节日/普通词库各占 50%，避免全天只聊节日。
- 第一次是角色+活动实例层面的“已显示问候”标记，只有气泡成功显示才记；请求失败、取消、忙碌不消费。显示记录跨重启保留，近期文案仍沿用已有逻辑。
- AI 问候已启用时，将实际本地日期、活动名称/阶段、角色语气、实际穿着及解锁状态作为受控上下文传入。不得仅凭穿着万圣装，就在普通日期声称“今天万圣节”。
- 除夕、正月初一、正月初二至十四使用同一服饰，可分不同问候阶段；圣诞 12/24 与 12/25 同理。服饰包不因此拆成两套。
- 节日语气保持角色特点；清明等日子的文案另审，不统一套“节日快乐”。没有完成文案的活动沿用普通问候，不编造祝福模板。
- WPF 原有问候挥手仍用当前服饰的普通 `waving`；Avalonia 不为此强加新互动。两端问候均不能触发或解锁彩蛋。
- 不新开 AI 服务、不改模型、不自动朗读。用户原来关闭主动问候或 AI 问候时仍保持关闭；AI 调用次数不会因新调度器增加，节日上下文仅带来少量已有请求的 token 增量。
- 请求返回时重新检查角色、日期/活动阶段、服饰状态版本、问候设置与忙碌条件；不一致就丢弃，等待下一次正常机会。

## 3. 节日清单与日历实现

| ID | 活动 | 识别与持续时间 | 素材顺序 |
| --- | --- | --- | --- |
| `new-year` | 元旦 | 公历 1 月 1 日 | 后续 |
| `laba` | 腊八 | 正常农历腊月初八 | 后续 |
| `spring-festival` | 除夕＋春节 | 下一农历年正月初一的前一天起，至正月十四结束；15 天、同一套 | 第三批 |
| `lantern` | 元宵 | 正常农历正月十五 | 后续 |
| `qingming` | 清明 | 当年清明节气所在民用日期 | 后续 |
| `labour` | 劳动节 | 公历 5 月 1 日 | 后续 |
| `children` | 儿童节 | 公历 6 月 1 日 | 后续 |
| `dragon-boat` | 端午 | 正常农历五月初五 | 后续 |
| `qixi` | 七夕 | 正常农历七月初七 | 后续 |
| `mid-autumn` | 中秋 | 正常农历八月十五 | 后续 |
| `national-day` | 国庆 | 公历 10 月 1–7 日，7 天 | 后续 |
| `double-ninth` | 重阳 | 正常农历九月初九 | 后续 |
| `halloween` | 万圣节 | 公历 10 月 31 日 | **首发五角色** |
| `winter-solstice` | 冬至 | 当年冬至节气所在民用日期 | 后续 |
| `christmas` | 平安夜＋圣诞节 | 公历 12 月 24–25 日，2 天、同一套 | 第二批 |

1. 使用 `[StartDate, EndExclusive)` 表示活动；每日 00:00 是本地日期边界，结束日 00:00 起退出。持续时间来自上述产品规则，不跟随调休/法定假期表。
2. 农历采用 `ChineseLunisolarCalendar`。从输入日期取得农历年，再取该年闰月；`GetMonth` 是含闰月的序号，不能直接拿来匹配节日月份。闰月序号及以后归一化月份，另存 `IsLeapMonth`；传统固定月日只匹配非闰月。
3. 除夕不写死腊月三十：以春节公历日期减一天得到起点，以正月十五得到排他终点；检查相邻候选农历年，正确包含旧农历年末的除夕。腊月廿九、三十两种情况都要覆盖。
4. 调用前检查支持范围；边界候选年份越界时单独跳过。农历失败不能阻断普通公历节日。世纪外/异常系统日期不导致桌宠启动失败。
5. 清明、冬至首版内置 2020–2035 年官方日期表及来源元数据；运行时离线。范围外跳过这两项并记录一次诊断，不用近似公式猜日期；后续补表不需要改核心触发器。
6. 日期按中国传统历法得到的月日，在用户的系统本地日期上庆祝；不按所在地重算农历朔日。中国标准日期与用户时区的含义在测试中固定，不能把“北京时间到午夜”和“系统本地午夜”混用。
7. 同日多个活动先筛掉无完整资源的项，再按优先级取一个视觉活动：春节、元宵、中秋、端午、七夕、重阳、腊八、清明、冬至、国庆、圣诞、万圣、元旦、劳动、儿童。中秋优先覆盖当日国庆，翌日国庆仍有效时恢复国庆；不轮播两套，也不增加手动解锁入口。
8. 活动内容分别标记服饰/动作与问候是否 Ready；识别到日期不代表已经制作内容。未 Ready 的项不自动穿衣、不暴露空选项；首发只发布万圣节的完整内容。

依据：[Microsoft ChineseLunisolarCalendar 文档](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.chineselunisolarcalendar?view=netframework-4.8.1)说明含闰月的月份序号；[香港天文台公历与农历对照表](https://www.hko.gov.hk/tc/gts/time/conversion.htm)用于离线节气表和独立验算。黄金测试值必须取自官方对照资料，不能让待测算法自己生成预期值；有官方修订时更新表版本与夹具来源。

## 4. 万圣节五角色制作清单

### 4.1 每位角色的服饰、动作与文案

| 角色 / 目录 | 常驻服饰方案 | 悬停专属动作，8 帧分镜 | 主动问候示例；每角色最终需 6 条 |
| --- | --- | --- | --- |
| E包 / `e-bao` | **绯夜小伯爵**：黑红燕尾礼服、酒红马甲、褶袖衬衣、短裤长袜和皮靴；替换运动服，保留白发蓝眼与少年比例 | **傲娇讨糖**：1–2 抱臂装酷，3–4 偷递糖袋，5–6 开心又装镇定，7–8 收势 | “万圣节到了，糖给你留了一颗。才不是特地准备的。” |
| 大肥鱼 / `dafeiyu` | **南瓜海盗船长**：三角帽、蓝橙金船长长外套、腰带、条纹袜靴；替换原女仆服，保留蓝发、耳鳍与鲸尾 | **南瓜泡泡糖**：1–2 双手托起，3–4 形成糖果泡泡，5–6 递出泡泡，7–8 摆尾收势 | “万圣节快乐呀，要不要歇一会儿，想想最喜欢的糖果？” |
| 马尔芙 / `marfu` | **月夜南瓜女巫**：黑橙分层南瓜裙、束腰、宽檐帽与长靴；完整替换黑绿校袍，保留金发绿眼与魔杖 | **南瓜灯咒**：1–2 举杖，3–4 点亮南瓜灯，5–6 灯绕半圈，7–8 收光 | “今晚给幸运魔法加一点南瓜味，愿你的事情都顺顺利利。” |
| 小贝 / `xiaobei` | **幽灵机械术士**：黑紫机能法袍、新内搭、薄荷荧光衣摆与战靴；替换原日常机能服，保留黑银发和机械猫耳 | **全息讨糖**：1–2 猫耳亮起，3–4 展开全息南瓜，5–6 接住像素糖，7–8 关闭界面 | “万圣节的糖果配额先留给你，休息时间也别忘了留一点。” |
| 咕嘎 / `gugugaga` | **南瓜糖果怪盗**：橙黑南瓜兜帽夹克、怪盗短裤与黄靴；整体替换企鹅连体服，保留黑发、机械发夹与银色领扣 | **咕嘎南瓜糖**：1–2 慢半拍歪头，3–4 捧起糖袋，5–6 收糖摇摆，7–8 收回糖袋 | “咕嘎！今天可以讨糖吗？我想先挑一颗最大的。” |

衣服设计先各做一张定稿，验证帽子不遮面、披风不盖关键辨识特征。道具需要在动作结束时收回，避免常驻糖袋遮住点击区域或影响其他招牌动作。现有动作中的魔杖、鲸尾、猫耳、企鹅鳍继续按角色原设定绘制。

### 4.2 每套服饰必须覆盖的资源

统一目录：`assets/holidays/halloween/<character-id>/`，首版共五个目录。

| 资源 | 路径约定 | 数量/要求 |
| --- | --- | --- |
| 基础 9 动作 | `animations/<action>.png` | idle、running-right、running-left、waving、jumping、failed、waiting、running、review |
| 互动 8 动作 | 同上 | lifted、landing、petting、poked、feeding、happy、sleeping、special |
| 额外 2 动作 | `extra/stumble.png`、`extra/special2.png` | 每项 8 帧 |
| 第二套动作 | `alternate/<action>.png` | lifted、landing、feeding、happy、sleeping、special、special2、stumble；E包/咕嘎再含 jumping，共 8/9 项 |
| 视线 | `gaze.png` | 16 张独立方向图，4×4，1920×2080，不做光流插值 |
| 节日悬停彩蛋 | `easter-egg.png` | 8 帧、4 FPS、不循环，3840×520 |
| 服饰预览/验收定稿 | `preview.png` | 480×520，可由合格 idle 首帧导出，无需新增头像设计 |
| 描述 | `outfit.json` | 稳定 OutfitId、角色/活动 ID、版本、动作路径/帧数/FPS/循环、对齐信息、动作名称 |

- 普通动作 PNG 统一单格 480×520、8 列；旧动作不足 8 帧时保留原实际帧数，其余透明填充。每套动作沿用相应源动作的时长和循环语义，不为了统一 8 帧而改跳跃落地、喂食转开心等行为。
- 与原角色逐帧匹配身体比例、脚底线、朝向、镜像规则。首尾与待机衔接；同一服装在跑动、视线、两套互动和睡眠中不突然恢复原装。
- 默认完整覆盖双套表现。原本允许左右镜像的动作可以复用素材；不把不对称的服饰、文字、道具随意镜像。以下预算不提前扣除复用收益。
- 不额外制作节日聊天头像、音效或语音包；现有角色头像保持身份识别。云雾效果由程序绘制，不计入角色绘图帧数。

### 4.3 按当前 dev 统计的帧位预算

这里统计需要检查的有效画面/帧位，不是人工逐张绘画工时，也不是磁盘大小承诺。沿用现有帧数、包含双套动作、暂不扣镜像复用：

| 角色 | 基础＋视线＋互动/额外 | 第二套动作 | 新彩蛋 | 预览 | 合计 | PNG 数量 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| E包 | 57＋16＋32＋16＝121 | 72 | 8 | 1 | **202** | 31 |
| 大肥鱼 | 121 | 64 | 8 | 1 | **194** | 30 |
| 马尔芙 | 121 | 64 | 8 | 1 | **194** | 30 |
| 小贝 | 136＋16＋16＝168 | 64 | 8 | 1 | **241** | 30 |
| 咕嘎 | 168 | 72 | 8 | 1 | **249** | 31 |
| **合计** | | | **40** | **5** | **1,080** | **152** |

还需 5 份 `outfit.json`、1 份总目录、1 份万圣词库（30 条）、1 份节气日期表。PNG 计数按“每动作单独一张图”组织，合图优化不减少画面一致性检查量。

节省成本优先从“只先做万圣节、先完成 E包定稿、共用变装效果、复用原动作构图、预览导出、批量验图”入手；本计划不通过取消跑步/视线/双套互动来压低数字。如果后续决定减少服饰动作变体，应另行收缩规格并重估，不把它悄悄作为验收豁免。

## 5. 状态、资源与代码改动

### 5.1 公共状态与接口约定

新增公共目录 `src/Holidays/`，内部文件不引用 WPF/Avalonia。保持职责清楚，不新增通用插件框架。

| 文件（拟新增） | 职责与供其他任务调用的接口 |
| --- | --- |
| `HolidayCalendar.cs` | `IList<HolidayOccurrence> Resolve(DateTime localDate)`；返回有序活动，含 Id、StartDate、EndExclusive、InstanceKey、Stage。月份归一化和范围防护集中于此 |
| `HolidayCatalog.cs` | `OutfitDefinition Find(string characterId,string outfitId)`、`OutfitDefinition Active(string characterId,IList<HolidayOccurrence> events)`；校验只读目录，选择已就绪素材。`OutfitDefinition` 定义动作/两套资源、视线和独立彩蛋，不复用伙伴 ID |
| `HolidayStateStore.cs` | `Load()`、`Save(HolidayState state)`；状态含 SchemaVersion、WardrobeDiscovered、每角色 Enabled/PreferredOutfitId、解锁集合、每活动手动覆盖/过渡/首问候已显示标记。提供唯一实例，所有变更串行保存 |
| `HolidayController.cs` | `Refresh(HolidayContext context)`、`SetEnabled(bool enabled)`、`SelectOwnedOutfit(string outfitId)`、`TryBeginHover(double randomValue)`、`CompleteHover(HolidayPlaybackTicket ticket)`、`CancelHover()`、`MarkGreetingShown(HolidayGreetingContext context)`；对外暴露 EffectiveOutfit、OwnedOutfits、WardrobeVisible、Revision 及 Changed 事件 |
| `HolidayPlayback.cs` | `Start(HolidayPlaybackTicket ticket)`、`Advance(double deltaSeconds)`、`Cancel()`；暴露 Frame/Active/Completed。只负责彩蛋单次播放，不负责普通动作播放器和变装时序 |
| `HolidayTransition.cs` | `Start(string targetOutfitId)`、`Advance(double deltaSeconds)`、`FinishImmediately()`；暴露遮罩参数及唯一提交换装时点，两端共用 800 ms 时序 |
| `HolidayGreetingPhrases.cs` | 按角色/活动阶段加载受控词库并按最近文本去重；定义 `HolidayGreetingContext`，由既有 GreetingService 消费 |

共享类型放在其所属文件附近：

- `HolidayContext`：PersonaId、可信 CharacterId、本地时间、MonotonicSeconds、CanPlayEasterEgg、CanAnimateTransition、角色资源版本。平台算好门控，公共层不能猜聊天窗口状态。
- `HolidayPlaybackTicket`：唯一 Token、当前角色/服饰/活动实例/版本、日期、开始时 Revision、可否解锁。无当前有效票据的完成回调无效；重复回调幂等。
- `HolidayGreetingContext`：角色、日期、活动实例/阶段、是否采用节日词库、当前穿着/拥有状态、Revision。旧的 GenerateAsync/Fallback 调用保留兼容重载，新重载多接收此对象；普通问候传 null。WPF 的记忆读取失败分支也要经过带上下文的 Fallback，不能绕过节日词库。
- `OutfitClipDefinition`：资源相对路径、FrameCount、Columns、FPS、Loop、单格尺寸、对齐信息。每种 action+variant 只能唯一映射；命中与绘制用同一已提交帧描述。
- 所有时间与随机输入允许在测试注入。用户不能通过参数直接指定“解锁”；开发测试通过内部构造器注入，不放生产菜单。

Revision 只在影响请求资格的日期/活动阶段、角色、服饰、资源版本或启用状态变化时增加；普通每秒 Refresh、动画帧推进、冷却计时和无变化的重复查询不能增加版本。开始/完成票据使用独立 Token，不能因登记自己的播放状态而使票据立即失效。否则 2 秒彩蛋和 8 秒问候都会被每秒日期检查误取消。

状态保存到当前应用数据目录下的 `holiday-state.json`，不硬编码成仓库工作目录。独立于 PetSettings，避免已有 `Settings.Copy()`/异步保存覆盖收藏。存储采用临时文件、原子替换、备份恢复；SchemaVersion、字段数、ID 长度和文件大小均有边界（首版 ≤1 MiB、≤4096 条活动记录），清理旧活动记录时不清理解锁集合。

已显示入口和解锁以单次事务保存。保存失败时进程内保留待保存结果，提示一次并在后续状态变更/退出时重试；只有落盘成功后才宣称“永久解锁”。损坏状态保留原文件并优先恢复备份；无法恢复时安全使用原装，不伪造收藏。移除内容只影响可用性，不删除收藏。旧轻量计划没有实现，无需为其假想 JSON 做迁移。

### 5.2 现有文件改动表

| 文件 | 计划改动 |
| --- | --- |
| `src/SkinCatalog.cs`、`src/BundledSkinCache.cs` | 添加可信的 `BuiltinCharacterId` 元数据。前三位注册时赋值，随附 loader 从注册目录赋值；旧个人 ID 合并时复制该元数据。用户导入包不因同名/同 ID 获得内置来源资格 |
| `src/PetWindow.cs` | 初始化控制器；在渲染前更新日期、切换视觉资源，接入悬停、取消、角色切换、生命周期与问候上下文。保持现有会话身份不变 |
| `src/PetHoliday.cs`（新增） | WPF partial 适配：有效素材解析、视觉帧提交、云雾层、可播门控、完成/取消回调、状态事件。复杂逻辑从主窗口隔离 |
| `src/PetRuntimeAnimation.cs` | 新业务状态/审批/恢复/完成反馈出现时取消彩蛋；避免动画被节日占用吞掉 |
| `src/HoverActions.cs` | 复用现有节日提供接口；将特殊候选的选择与普通候选分开，支持 30%/20% 概率，不让普通菜单复用特殊动作 |
| `src/AdditionalInteractions.cs`、`src/InteractionAtlas.cs` | 由外观适配器优先解析相应换装帧；只有确需抽出解码/采样辅助时局部调整，不重写原装/导入包行为 |
| `src/SettingsPage.cs` | 在伙伴选择下面接入新区域，监听解锁/换角色/开关变化并实时刷新，释放时退订事件 |
| `src/HolidayOutfitPanel.cs`（新增） | WPF 小控件，仅开关、已拥有服饰下拉和必要状态文本。没有解锁按钮/动作预览按钮 |
| `src/GreetingService.cs`、`src/GreetingSchedule.cs` | 兼容旧调用，加入节日上下文、本地词库选择及去重；原间隔调度保持不变 |
| `desktop/DeskPet.Desktop/SpriteAssets.cs` | 保存可信角色来源，支持 outfit clip/视线/两套动作；缓存键加入资源来源、OutfitId、版本；仍用同一帧生成命中掩码 |
| `desktop/DeskPet.Desktop/PetWindow.cs` | 改为 partial，Tick/悬停/取消/问候接入公共控制器；不修改固定四格互动菜单来展示节日动作 |
| `desktop/DeskPet.Desktop/PetHoliday.cs`（新增） | Avalonia 视觉适配、云雾绘制、生命周期门控及控制器事件 |
| `desktop/DeskPet.Desktop/InteractionPlayback.cs` | 解析器接入当前视觉资源；保证切衣不重置业务动作、不破坏 feeding→happy→idle 和落地链 |
| `desktop/DeskPet.Desktop/DesktopController.cs` | 统一持有节日状态，SelectSkinAsync/退出时通知适配器，给设置窗口共享同一实例 |
| `desktop/DeskPet.Desktop/SettingsWindow.cs` | `Partner()` 的角色选择下方放入 `HolidayOutfitPanel`，角色切换、恢复角色后同步刷新 |
| `desktop/DeskPet.Desktop/HolidayOutfitPanel.cs`（新增） | 与 WPF 相同语义的控件，不扩展成完整衣橱页面 |
| `desktop/DeskPet.Core/DeskPet.Core.csproj` | Link 新公共 Holidays 文件，仅公共代码进入 Core |
| `desktop/DeskPet.Desktop/DeskPet.Desktop.csproj` | 将已发布 holidays JSON/PNG 编入资源，保持相同逻辑路径 |
| `Build.ps1` | 注册新测试 Target 与 Windows 资源；保留 UpdateApplyTests 的轻量夹具，不能把全套图片塞回该夹具 |
| `tools/validate_holiday_assets.py`（新增） | 校验清单、尺寸、帧数、透明背景/非空帧、来源动作覆盖与资源引用；输出人工检查接触表 |
| `desktop/DeskPet.Tests/DeskPet.Tests.csproj`、`PortableTests.cs` | 显式纳入公共测试并在 runner 调用，不能只创建测试文件 |
| `desktop/DeskPet.Desktop.Tests/Program.cs` | 注册 UI/播放/命中 Headless 用例；需要时新建同目录小型用例文件 |
| `docs/holiday-outfits.md`（新增，实施时） | 用户使用说明、内容制作格式及无手动触发规则；版本记录在功能真实完成后更新 |

`Package.py`、`tools/runtime_package.py`、`tools/mac_package.py` 先做资源覆盖检查，只有现有内嵌机制确有缺口才修改。首版不修改普通角色包导入格式，也不把服饰导出成新伙伴。

### 5.3 渲染与性能边界

- 外观解析顺序：当前有效 Outfit → 对应 action/variant/gaze → 已提交帧。原装与自定义导入路径继续走现有解析器。缺少必需动作时整套不 Ready，不能某个动作悄悄穿回原装。
- 先完成素材读取及描述校验，再原子切换绘图与命中数据；禁止图已换、命中仍用原衣服的过渡帧。预览、阴影、镜像、缩放也使用相同对齐约定。
- 换装不调用 SelectSkin、不清会话、不更换 PersonaId；切换时保留当前动作及归一化播放进度，不能把喂食、睡眠、拖动或工作状态重置成 idle。
- 仅懒加载当前角色/外观和少量最近动作。8 帧 RGBA 图约 7.6 MiB，另有命中掩码与 UI/GPU 开销；禁止同时解码五角色全部服饰。先沿用当前三动作缓存思路，视线单独有界缓存，换角色/资源版本时释放旧引用。
- 安装包压缩大小与额外常驻内存目前均为 [待测]，不可由 PNG 数量臆算。验收报告记录换装前后、遍历所有动作后的内存以及三次角色/服饰往返；不得持续无界增长。

## 6. 分任务实施顺序与验收

每任务按“新增针对性失败用例 → 跑到预期失败 → 实现 → 通过该任务检查 → 记录差异”推进。以下命令为本次实施检查清单；分批提交和临时分支 CI 按用户授权执行，发布另行处理。

### 任务 1：离线日历、目录与收藏状态

**Files:** 新增 `src/Holidays/HolidayCalendar.cs`、`HolidayCatalog.cs`、`HolidayStateStore.cs`；新增 `assets/holidays/catalog.json`、`assets/holidays/calendar/solar-terms.json`；新增 `tests/HolidayCalendarTests.cs`、`HolidayStateTests.cs`、`tests/fixtures/holidays/calendar-golden.json`；修改两测试 runner/项目及 Build.ps1。

**Interfaces:** 产出第 5.1 节的 `Resolve`、`Find/Active`、`Load/Save` 及相应类型，供任务 2–6 使用。

- [ ] 固定黄金样本：元旦/万圣前后一天；国庆 9/30、10/1、10/7、10/8；圣诞 12/23–12/26；至少十个不同年份的农历/节气正例和相邻日反例，包含腊月廿九/三十、闰月当月与之后月份。
- [ ] 添加 `LeapMonthIsNotFestival`、`SpringFestivalIncludesEveThroughDay14`、`OverlappingHolidaysPreferReadyContent`、`OutOfRangeKeepsGregorianRules`；预期值独立取官方表，不由 Resolve 生成。
- [ ] 添加 `UnlockAndDiscoveryPersistTogether`、`RemovingOutfitDoesNotForgetOwnership`、`FailedWriteDoesNotClaimDurability`、`CorruptStateRestoresBackup`、`PruningKeepsUnlocks`；验证关闭开关和手动偏好往返保存。
- [ ] 运行对应测试，确认缺实现而失败，再实现公共日历、严格目录和原子状态保存。
- [ ] 使用 `./Build.ps1 -Target HolidayCalendarTests`、`./Build.ps1 -Target HolidayStateTests` 通过目标；Portable runner 同样执行这些用例。

### 任务 2：E包完整服饰资源与双端视觉解析

**Files:** 第 5.2 节的角色来源/资源解析文件；新增两端 `PetHoliday.cs` 中的视觉部分、`tools/validate_holiday_assets.py`、`tests/HolidayAssetTests.cs`、`tests/HolidayVisualSmoke.cs`；先制作 E包目录，最后由任务 7 扩到五位。

**Interfaces:** 消费 `OutfitDefinition/OutfitClipDefinition`；产出“给定角色、OutfitId、action、variant、frame，取得唯一视觉帧”的两端适配能力，供任务 3/4 使用。

- [ ] 按第 4 节完成 E包服饰定稿，先验证 idle、gaze、彩蛋与一组原版/新版互动，随后补齐其余动作；未补齐前只在开发测试使用，不标 Ready。
- [ ] 添加 `LegacyPersonaKeepsTrustedCharacter`、`ImportedSameIdDoesNotUseBuiltinOutfit`、`AllRequiredVariantsResolve`、`GazeHasSixteenDirections`、`VisibleFrameAndHitMaskAgree`，包含左右镜像与透明边缘。
- [ ] 测试先失败后实现来源元数据和视觉解析；验证换衣不会调用角色切换、改变人格或关闭工作会话。
- [ ] 校验脚本通过 E包完整目录，手工检查跑步/提起/落地/喂食/开心/睡眠/业务状态，不出现原装闪回或脚底跳位。
- [ ] 运行 `./Build.ps1 -Target HolidayAssetTests`、`./Build.ps1 -Target HolidayVisualSmoke`，以及 Avalonia 对应 Headless 用例。

### 任务 3：控制器、零点切换与云雾过渡

**Files:** 新增 `src/Holidays/HolidayController.cs`、`HolidayTransition.cs`、`tests/HolidayRuntimeTests.cs`；修改两端 PetWindow/PetHoliday、DesktopController。

**Interfaces:** 消费任务 1 的日历/状态、任务 2 的视觉解析；产出 `Refresh/SetEnabled/SelectOwnedOutfit`、EffectiveOutfit、Revision、Changed 和过渡提交点。

- [ ] 添加 `MidnightAppliesOutfitWithoutUnlock`、`ResumeDuringHolidayCatchesUp`、`ResumeAfterHolidayDoesNotUnlock`、`MultiDayEventDoesNotTransformDaily`、`ManualOverrideSurvivesRestart`、`DisabledOutfitStaysDisabledNextEvent`。
- [ ] 添加 `BusyStateKeepsItsActionDuringSwap`、`TransitionCommitsAtMostOnce`、`TimeRollbackDoesNotReplayDiscovery`、`OverlappingDayReturnsToLongerEvent`；对暂停/隐藏状态也检查日期刷新。
- [ ] 测试失败后接入 ≤1 秒日期检查、系统恢复校正、偏好覆盖与 800 ms 共用云雾时序，不新增高频定时器。
- [ ] 用模拟时钟跨午夜与活动末日，验证退出后恢复先前选择；打断云雾后只留下最新外观，无浮层或输入阻挡。
- [ ] `./Build.ps1 -Target HolidayRuntimeTests` 通过，双端人工演示零点/恢复/忙碌三类路径。

### 任务 4：仅悬停彩蛋、成功解锁与业务抢占

**Files:** 新增 `src/Holidays/HolidayPlayback.cs`、`tests/HolidayHoverTests.cs`；修改 HolidayController、HoverActions、PetRuntimeAnimation、两端 PetHoliday/PetWindow；扩展 `tests/HolidayVisualSmoke.cs`。

**Interfaces:** 消费任务 3 的上下文和视觉状态；实现 `TryBeginHover/CompleteHover/CancelHover`、`HolidayPlaybackTicket` 与完成回调，供任务 5 显示解锁结果。

- [ ] 对固定随机值测试 `[0,0.30)` 与 `[0,0.20)` 概率区间、边界 0.30/0.20 落普通分支；无需靠随机大样本证明概率。
- [ ] 添加 `OneRollPerEntry`、`CooldownRequiresReentry`、`FirstFrameStartsCooldown`、`InterruptedPlaybackDoesNotUnlock`、`CompletionUnlocksOnlyCurrentCharacter`、`LateCallbackCannotUnlockNewRole`。
- [ ] 添加 `MenuPaletteAiCannotStartHolidayAction`、`BusyWorkPreemptsHoliday`、`MidnightDuringPlaybackCancelsUnlock`；明确全会话/审批/恢复/队列均在忙碌门控内。
- [ ] 测试失败后实现一次进入门控、60 秒冷却及完成事务；普通候选抽中不写解锁，资源准备失败不改状态。
- [ ] `./Build.ps1 -Target HolidayHoverTests` 与 HoverSleepSmoke/InteractionTests 通过；两端检查第一帧、拖动打断及结束回到当前业务/空闲状态。

### 任务 5：设置入口永久发现与服饰选择

**Files:** 新增两端 `HolidayOutfitPanel.cs`、`tests/HolidaySettingsSmoke.cs`；修改 SettingsPage、Avalonia SettingsWindow 及 Headless 测试注册。

**Interfaces:** 只消费控制器的 WardrobeVisible、OwnedOutfits、EffectiveOutfit、Changed、SetEnabled、SelectOwnedOutfit，不直接读写 JSON。

- [ ] 添加 `BeforeUnlockPanelIsCollapsed`、`DetectedHolidayAloneDoesNotRevealPanel`、`CompletedEggRevealsOpenSettings`、`DiscoverySurvivesRestart`、`PickerContainsOnlyCurrentOwnedOutfits`。
- [ ] 添加开关关闭后原装/下拉禁用、切到无收藏角色时的空态、删除/恢复角色保留收藏、已拥有但资源损坏时的禁用项用例。
- [ ] 测试失败后在两端现有伙伴下拉框右侧添加等宽“衣柜”，含默认与已解锁服饰的缩略图；发现前保留原布局，发现后按伙伴独立选择，使用现有主题控件，窗口已打开时解锁也能刷新，不要求重启。
- [ ] 验证换衣不清空设置页未保存的人格/模型表单，不触发 SelectSkin；重复开关设置窗口无事件订阅泄漏。
- [ ] `./Build.ps1 -Target HolidaySettingsSmoke` 及相关设置导航/主题检查通过；确认没有显示锁定服饰或彩蛋播放按钮。

### 任务 6：五角色节日问候

**Files:** 新增 `src/Holidays/HolidayGreetingPhrases.cs`、`assets/holidays/halloween/greetings.json`、`tests/HolidayGreetingTests.cs`；修改 GreetingService、两端问候调用、HolidayController；按需扩展现有 AiGreetingTests。

**Interfaces:** 产出 HolidayGreetingContext；GreetingService 增加接收该上下文的兼容重载，旧签名行为保持；气泡显示成功后调用 `MarkGreetingShown`。

- [ ] 审核五角色共 30 条文案；无须额外头像/音频，非 Ready 活动只使用普通问候。
- [ ] 添加 `FirstHolidayGreetingUsesHolidayPool`、`SubsequentGreetingsUseHalfProbability`、`OutfitOutsideDateDoesNotClaimHoliday`、`AiOffNeverCallsTransport`、`AiFailureFallsBackToHolidayPhrase`。
- [ ] 添加 `DateRoleOrRevisionChangedDiscardsReply`、`UnchangedDateProbeDoesNotCancelRequest`、`MemoryReadFailureKeepsHolidayFallback`、`QuietModeAndIntervalsUnchanged`、`GreetingNeverUnlocksOutfit`；验证关闭问候不影响日期换装。
- [ ] 测试失败后加入受控上下文、词库回退和成功显示记账；保持现有 8 秒 AI 超时及近期文本去重。
- [ ] `./Build.ps1 -Target HolidayGreetingTests`、GreetingTests、AiGreetingTests 通过；用假 API 验证，不以真实付费请求作为默认测试方式。

### 任务 7：补齐四角色、资源包装和发布前验证

**Files:** 补齐其余 `assets/holidays/halloween/<character-id>/`，更新 catalog；完成 Build.ps1、Desktop csproj 资源声明、`docs/holiday-outfits.md`；包装脚本仅在发现缺口时改。

**Interfaces:** 消费前六任务的完整契约；每个角色达到动作、视线、彩蛋、文案全部就绪后才将对应条目标 Ready。

- [ ] 按第 4 节逐一完成大肥鱼、马尔芙、小贝、咕嘎；每位交付整套图、描述、接触表和深浅背景动画检查结果。
- [ ] 执行 `python tools/validate_holiday_assets.py`；五位均通过，不能以第一位合格替代其他四位的人物/动作审查。
- [ ] 执行公共、WPF 和 Avalonia 相关测试；双端逐个检查一次未解锁→临时穿着→悬停完成→设置常驻→跨日手选的完整链路。
- [ ] 验证发布包实际包含所有 Ready 资源、没有测试夹具/接触表，测量体积和加载内存；UpdateApplyTests 不嵌完整图集，现有更新包上限不因素材增加直接放宽。
- [ ] Windows 真机检查跨午夜/休眠；macOS 真机检查窗口透明命中、视线、换装、打断及恢复。无真机时在交付报告明确未验证项。
- [ ] 只在以上完成后确认版本说明和最终 CI；用户已授权临时分支推送，合并、标签及 Release 不在本次开发授权中。

实施验证命令参考（仓库根目录，新增 Target 注册后才可执行）：

```powershell
./Build.ps1 -Target HolidayCalendarTests
./Build.ps1 -Target HolidayStateTests
./Build.ps1 -Target HolidayAssetTests
./Build.ps1 -Target HolidayRuntimeTests
./Build.ps1 -Target HolidayHoverTests
./Build.ps1 -Target HolidaySettingsSmoke
./Build.ps1 -Target HolidayGreetingTests
./Build.ps1 -Target HolidayVisualSmoke
dotnet run --project desktop/DeskPet.Tests/DeskPet.Tests.csproj
dotnet run --project desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj
python tools/validate_holiday_assets.py
```

运行有 UI 的 Smoke 目标需可交互桌面。现有 GreetingTests、AiGreetingTests、InteractionTests、HoverSleepSmoke、BundledSkinTests、ImportedSkinTests、SpriteAlignmentTests、UpdateApplyTests 按受影响范围回归；验证一次通过后，不无理由重复全量构建。

## 7. 粗略工期与推荐推进方式

以下是工程排期估计，不是实测工时或完成日期承诺。假设一位熟悉仓库的开发者；素材用现有原画参考＋AI 辅助批量制作＋人工修正；dev 接口不发生大改，且能取得两端验证环境。图像一致性返工是最大不确定项。

| 交付范围 | 估算工作量 | 说明 |
| --- | --- | --- |
| E包体验样机 | **4–6 个工作日** | 打通零点/云雾、悬停解锁、衣橱和问候；只用小部分验证素材，不等于完整可发布角色 |
| 全部共享代码与双端接入、回归 | **10–15 人日** | 包含样机中的代码工作，不与上一行相加 |
| 万圣五角色素材制作与逐帧修正 | **8–15 人日** | 约 1,080 画面/帧位，包含定稿、动作图、视线、预览和文案；有合格批量结果时更快，返工可能更久 |
| 一个人串行完成全部首发 | **18–30 个工作日，约 4–6 周** | 代码与素材估算相加，未包含外部等待和严重返工 |
| 开发与素材制作有人并行 | **约 3–5 周自然排期** | 定稿和样机先行，再并行制作，末尾留整体验收时间；不是两边工时简单取最小值 |
| 后续再加一个五角色完整节日 | **约 1.5–3 周素材及接入验收** | 大部分代码复用；服装/动作仍要重新制作，不按“加一条日期配置”估价 |

建议将万圣节首发拆成两个可判断成本的里程碑：

1. **先完成 E包样机和服饰定稿。** 取一组基础/双套互动/视线/彩蛋观察批量制作成功率，再把 8–15 人日的素材估算校准；若成本过高，此时缩范围最便宜。
2. **确认效果后完成 E包全部动作，再扩展四位。** 共享代码只做一遍，后面每位主要是素材与验收；圣诞、春节以及其他节日按顺序单独交付，不把十五个节日的全套美术塞进第一期。

2026 年万圣节距离本计划日期较近，不能把“五角色完整首发赶上今年 10 月 31 日”当作上述估算的默认承诺。若以后明确要求赶期，应另定单角色或部分素材的预览范围。

## 8. 原计划交付记录与当前边界

原计划分支仅形成设计与此前基础阶段记录。本次依据用户后续授权实施完整功能，目标 `0.10.3-dev.1`；验收以实际测试、完整素材和各临时分支 CI 结果为准，不把计划文字当作已完成证据。
