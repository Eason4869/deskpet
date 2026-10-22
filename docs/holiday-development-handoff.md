# 节日功能限时开发交接

本轮用户授权：开发至北京时间 2026-10-22 17:45，停止产品开发后整理并推送临时分支。

- 临时分支：`codex/holiday-outfit-20261022-plan`。
- 独立 worktree：`E:\workspace\codex\deskpet\work\holiday-outfit-plan`；原 dev 工作区保留。
- 实施基线：远端 dev `6c42f311a8425b7e8e2156093b9514b30d239872`；此前计划基于 `59be54e`，以新的交接状态为准。
- 规格：[完整开发计划](plans/2026-10-22-holiday-outfit-unlock-and-greetings.md)。轻量 MVP 文件保留为历史，不是当前实现目标。

## 已完成

1. **离线节日识别**：`src/Holidays/HolidayCalendar.cs`，提供 `Resolve(DateTime localDate)`，返回有序活动及起止日期、实例键、问候阶段。15 项规则均可识别；国庆 7 天、圣诞 2 天、除夕至正月十四共 15 天。
2. **农历与边界**：排除闰月同名节日，归一化闰月之后的月份；除夕由春节减一天求得，兼容廿九/三十；跨农历年、极值日期和支持范围外仍保留公历规则。
3. **离线节气资料**：`assets/holidays/calendar/solar-terms.json` 收录香港天文台 2020–2035 年清明/冬至日期。范围外不猜测。`HasSolarTermDates(year)` 和 `SolarTermWarning` 供未来运行时提示使用。
4. **独立日期验算**：`tests/fixtures/holidays/calendar-golden.json` 包含 519 条用例、19 份官方来源及原文 SHA256；另有公历活动/时段/极值检查。`tools/update_holiday_calendar.py` 可显式刷新资料；应用和普通构建从不联网取日历。
5. **收藏存储底层**：`src/Holidays/HolidayStateStore.cs`，按角色保存开关、已拥有服饰、手动偏好及活动内覆盖/变装/首问候标记，全局保存衣橱发现状态。重复解锁幂等，旧活动最多保留 4096 条，清理不删收藏。
6. **数据保护**：单实例串行写入、临时文件＋原子替换、备份恢复、损坏文件隔离、1 MiB 上限、ID/数量检查、未知未来版本禁止覆盖。临时读取失败短重试后保护原文件；先读版本头，再按 v1 解析字段。
7. **构建与回归接入**：Windows Build.ps1、新的 Framework 测试 Target、DeskPet.Core 共享编译、PortableTests runner、Windows CI 测试列表均已注册。

**当前应用界面没有节日入口，也不会自动换装。** 这批是可复用、已测试的底层，尚未接到实际桌宠运行循环；没有生成任何服饰 PNG，也没有把缺素材的节日标成可发布。

## 未完成与接手顺序

| 原计划任务 | 状态 | 下一步 |
| --- | --- | --- |
| 1 日历、目录、收藏 | **部分完成** | 日历/存储已完成；还缺 HolidayCatalog、OutfitDefinition、素材 Ready 校验及重叠活动按 Ready 过滤 |
| 2 E包视觉闭环 | 未开始 | 核对最新角色来源，增加可信 BuiltinCharacterId；制作 E包定稿及完整动作，接入两端渲染/命中 |
| 3 控制器、零点、云雾 | 未开始 | 实现 HolidayController/Transition，区分临时体验与永久拥有；安全帧换装、跨日与恢复补判 |
| 4 悬停与解锁 | 未开始 | 30%/20% 概率、一次进入、60 秒冷却、票据与完成事务；所有业务忙碌抢占 |
| 5 隐藏衣橱 UI | 未开始 | 第一次成功解锁后常驻显示；每角色仅已拥有项；切衣不切伙伴身份 |
| 6 节日问候 | 未开始 | 五角色共 30 条万圣词库，兼容 GreetingService 重载、异步返回版本校验 |
| 7 五角色及发布验收 | 未开始 | 其余四位美术、全动作 QA、内存/包体测量、Windows/macOS 真机、文档与后续发布 |

推荐下一次先补 **HolidayCatalog＋可信角色来源**，然后只用 E包打通实际界面闭环；不要直接对当前五角色做批量美术后才验证渲染。完整素材预算仍按原计划约 1,080 画面/帧位评估，当前没有消化这部分工作量。

## 接口及集成注意事项

- 日历接收“系统本地民用日期”，不负责 UTC 转换；实例键是 `holidayId:yyyyMMdd`。`Resolve()` 返回所有匹配项的固定优先级，尚未根据服饰可用性过滤。
- 春节阶段为 `eve`、`new-year`、`festival`；圣诞为 `eve`、`day`；其余 `day`。这是未来问候上下文的输入，当前不触发发言。
- `HolidayStateStore` 构造参数是目标 JSON 文件完整路径。运行时应由应用控制器创建唯一实例，使用实际应用数据目录，先 Load，再按 Warning 决定如何提示。
- `HolidayState.Unlock(characterId,outfitId)` 只是存储模型操作，没有检查日期/悬停/资源票据。**只能由未来控制器在成功完整演出后调用，不能在零点检测或设置下拉中直接调用。**
- `Save` 写入失败会抛异常，保留内存对象；“只提示一次并重试”的用户体验属于未来控制器，当前没有后台重试队列。永久解锁提示必须在 Save 成功后显示。
- 活动记录由调用者填充；本轮没有实现活动去重、60 秒冷却、日期探测和角色资格判断。存储中存在字段，不等于对应运行时行为已经完成。
- 后续对角色 ID 使用可信内置来源，不能根据显示名或用户导入包的同名 ID 借用内置服饰。
- 原计划基线是 0.10.1-dev.1；本分支实际基线为 **0.10.2-dev.3 / 6c42f31**，已包含其他人的图片工作流等更新。未修改原 dev 工作目录或版本号。

## 验证与已发现问题

- Windows Framework：`HolidayCalendarTests` **547 passed**；`HolidayStateTests` 审查修复后 **21 passed**；原 `GreetingTests` 基线 **19 passed**。
- Windows 应用：`./Build.ps1 -Target App -OutputDirectory work/holiday-app-build` 编译成功；没有启动 UI 或发布安装包。
- .NET 10：一次完整 PortableTests 在审查前退出码 0；审查修复后的最终完整回归 **3,508 项检查通过，退出码 0**，结果见随附 `holiday-validation.json`。
- `git diff --check` 通过；历表刷新脚本通过 Python 语法编译。构建中存在基线已有的 CS4014/CS0649、平台分析等警告，未在本限时任务中改动无关代码。
- Windows 受限沙箱阻止部分临时文件重命名；Framework 状态测试和完整 PortableTests 在允许临时文件/本地回环夹具的普通权限下完成。不把沙箱限制误报为产品存储失败。
- 初次 .NET 命令使用 PATH 内仅有运行时的 dotnet，后改用仓库已有 SDK；第一次恢复受网络限制，随后使用已有 NuGet 缓存完成。一次把 restore 参数传给 run 的调用返回 Usage，已纠正，不计入通过结果。
- 没有运行 WPF 全部 UI Smoke、Avalonia Headless UI、macOS 真机、安装器或远端 CI。临时分支 push 不在现有 Windows push 触发白名单内；创建 PR/合入 dev 后相关 CI 才会自动运行。
- 独立只读审查发现两项 P2：临时共享冲突误隔离、未来版本字段形状变化绕过版本保护。两项均补了先失败后通过的回归并修复；无其他审查遗留项。

可重复验证（仓库根目录）：

```powershell
./Build.ps1 -Target HolidayCalendarTests
./Build.ps1 -Target HolidayStateTests
./Build.ps1 -Target App -OutputDirectory work/holiday-app-build
dotnet run --project desktop/DeskPet.Tests/DeskPet.Tests.csproj -- --report-json work/holiday-portable-report.json
```

本机 SDK 不在 PATH，需要时使用 `E:\workspace\codex\deskpet\work\dotnet\dotnet.exe`（10.0.402），已有包缓存在 `E:\workspace\codex\deskpet\work\nuget-packages`。网络不可用时设置 `NUGET_PACKAGES`，先成功 restore，再 `run --no-restore`。无需安装新 SDK 或修改系统配置。

## 交付范围

保留独立 worktree 方便继续；原工作目录两个计划文件仍保留。推送临时分支即可，不合并 dev/main、不打版本标签、不发布 Release。代码已于 2026-10-22T17:42:21+08:00 冻结，早于 17:45 截止；提交和最终验证信息记录在交接摘要，未完成内容按上表继续。

## 执行记录与决策

- Ruling: 用户最新授权覆盖计划中的“本轮仅计划”；按 17:45 硬截止执行，允许部分完成，未完成任务明确交接。
- Ruling: 原目录 dev 正在其他开发，使用已忽略的 `work/` 内独立 worktree；不修改主目录文件。
- Ruling: 远端 dev 已更新，临时分支先快进到最新远端基线再实施；代价是后续 UI 接口需要以最新版本重查。
- Pre-flight: 任务 1 的 Resolve/Load/Save 供控制器和问候使用；保持 UI 无关。其他任务未开始，无接口冲突变更。
- Ruling: 本轮仅实现任务 1 中可以独立验收的日历与状态；不为了凑可见效果接入缺素材的 UI。代价是本分支暂时没有用户可见的节日行为，后续工作见上表。
- Ruling: Bash 工具因 Windows 沙箱的 MSYS IPC 限制不可用，使用 PowerShell 写同格式进度记录；交接正文保留完整决策，避免依赖忽略目录。
