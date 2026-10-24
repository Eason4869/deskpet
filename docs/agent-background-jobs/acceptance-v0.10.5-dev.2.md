# 0.10.5-dev.2 验收记录

当前状态：功能、独立代码审查及本地默认回归已完成；`efdc0458c88888906795021ac4518540138afafd` 的 macOS 双架构 CI 成功，Windows 仅新增管理器测试的时序断言失败，后续阶段未运行。已确定性复现并修正测试，生产代码未变化；新提交的完整 CI 仍须通过。本记录尚未批准合入 dev 或标记整版完成。

## 证据类型

- 完整本地默认回归：共享 .NET 8,911 项、Avalonia 3,094 项，均退出码 0。报告和日志位于 `work/agent-jobs-validation/s17-final-shared.json`、`s17-desktop-default-final2.log`；以下专项说明平台和证据边界。
- Framework WPF 真实 Windows 窗口：工作设置66、引导26、完整薄资源角色集成95、任务UI24、应用退出39；最新管理器65项包含十二项最终日志／元数据／退出竞态回归。引导的包传输使用受控 HttpClient 响应，完整素材通过真实原生解码和原子安装，不冒充外网下载。
- Avalonia Headless：任务UI31、持久绑定36；引导原有16项再补完整结束路径为18项。控制级PNG来自实际控件，不能证明系统窗口或外网。原生素材／角色加载专项25使用实际素材解码和受控传输。
- Avalonia Windows独立实际 HWND 宿主：原生任务／独立Edge预览9、退出30，包含 normal / held / writer / kill / direct 五种独立进程；既有图像退出3/3/2。浏览器使用测试隔离目录，未证明用户默认浏览器关联。
- macOS：只运行共享逻辑、非执行UI、原生素材桥和成品启动。Intel、Apple Silicon 分开验收；不执行或宣称通过 Windows Job/AppContainer。
- 代码审查：全分支独立审查发现的EOF与角色更新缺陷已复现并修复；后续最终保存及退出竞态也通过复查。审查通过不能替代目标提交CI。

## L01–L24

| ID | 证据入口 | 最终状态 |
| --- | --- | --- |
| L01–L07 | verify_localization完整审计；LocalizationTests / MissingResourceTests / LanguagePackTests | 4,499键、5,113候选、零未分类／缺词；最终CI待确认 |
| L08–L10 | 状态解耦、菜单、旧提供商配置／历史回归 | 最终CI待确认 |
| L11–L12 | 双端13语言完整UI、任务和引导矩阵，JSON基线与文字清单 | 最终CI待确认 |
| L13–L16 | 格式／取消／历史回归；用户追加授权后的默认人格和回复语言测试 | 最终CI待确认 |
| L17–L20 | 资源引用、独立文案基线、App/helper/Core实际嵌入哈希、便携／安装／Mac成品 | 最终CI待确认 |
| L21–L24 | 浅深／窄窗／重开、既有核心流程、默认入口登记、分平台CI | 最终CI待确认 |

L16/L24的原始“提示词、版本不改”是dev.1重构边界；dev.2的语言、人格、版本和功能增量另有用户明确授权，不以旧边界拒绝或隐藏追加需求。自定义人格、历史与角色稳定身份保留。机器结构检查和截图复核不等于13语言母语人工审校。

## A01–A32

| ID | 实施证据入口 | 最终状态 |
| --- | --- | --- |
| A01 | AgentJobProcessTests / AgentJobIntegrationTests；两端原生任务窗口 | 最终提交双平台 CI 待确认 |
| A02 | AgentJobUiTests / AgentJobUiChecks；WorkBindingUiChecks | 最终提交双平台 CI 待确认 |
| A03 | AgentJobIntegrationTests / AgentJobSessionTests | 最终提交双平台 CI 待确认 |
| A04 | AgentJobProcessTests；两端原生预览与 exit0/7 时间报告 | 最终提交双平台 CI 待确认 |
| A05 | AgentJobManagerTests / AgentJobIntegrationTests / AgentJobSessionTests | 最终提交双平台 CI 待确认 |
| A06 | AgentJobToolTests / AgentJobSessionTests；macOS 工作会话能力门禁 | 最终提交双平台 CI 待确认 |
| A07 | AgentJobManagerTests / AgentJobSessionTests / AgentJobIntegrationTests | 最终提交双平台 CI 待确认 |
| A08 | AgentJobToolTests / AgentJobSessionTests；双端原会话任务卡片 | 最终提交双平台 CI 待确认 |
| A09 | AgentJobProcessTests | 最终提交双平台 CI 待确认 |
| A10 | AgentJobProcessTests / AgentJobManagerTests | 最终提交双平台 CI 待确认 |
| A11 | AgentJobProcessTests | 最终提交双平台 CI 待确认 |
| A12 | AgentJobProcessTests / AgentJobManagerTests | 最终提交双平台 CI 待确认 |
| A13 | AgentJobManagerTests / AgentJobIntegrationTests | 最终提交双平台 CI 待确认 |
| A14 | AgentJobManagerTests / AgentJobStoreTests | 最终提交双平台 CI 待确认 |
| A15 | AgentJobLogTests | 最终提交双平台 CI 待确认 |
| A16 | AgentJobLogTests | 最终提交双平台 CI 待确认 |
| A17 | AgentJobStoreTests / AgentJobLogTests | 最终提交双平台 CI 待确认 |
| A18 | AgentJobManagerTests | 最终提交双平台 CI 待确认 |
| A19 | AgentJobStoreTests / AgentJobLogTests | 最终提交双平台 CI 待确认 |
| A20 | AgentJobManagerTests；结构化 LogWarning | 最终提交双平台 CI 待确认 |
| A21 | AgentJobLifecycleUiTests / AgentJobExitUiChecks；原有图片退出专用路由 | 最终提交双平台 CI 待确认 |
| A22 | AgentJobProcessTests / AgentJobLifecycleUiTests / AgentJobExitUiChecks | 最终提交双平台 CI 待确认 |
| A23 | AgentJobStoreTests | 最终提交双平台 CI 待确认 |
| A24 | AgentJobProjectTests / WorkBindingUiChecks / AgentJobLifecycleTests | 最终提交双平台 CI 待确认 |
| A25 | AgentJobSandboxTests | 最终提交双平台 CI 待确认 |
| A26 | AgentJobViewStateTests；双端任务卡片及原生预览 | 最终提交双平台 CI 待确认 |
| A27 | WPF Chromium / Avalonia 独立 Edge 原生窗口与 TCP 服务 | 最终提交双平台 CI 待确认 |
| A28 | AgentJobUiTests / AgentJobUiChecks；13语言浅深窄窗截图 | 最终提交双平台 CI 待确认 |
| A29 | AgentJobViewStateTests / AgentJobSessionTests；应用级完成通知 | 最终提交双平台 CI 待确认 |
| A30 | Avalonia Windows HWND 原生9/30；Mac有效配置及共享直调拒绝 | 最终提交双平台 CI 待确认 |
| A31（P1） | AgentJobProcessTests 的100次原生启动及句柄/名额样本 | 最终提交双平台 CI 待确认 |
| A32 | AgentJobUiTests / AgentJobUiChecks | 最终提交双平台 CI 待确认 |

A31为P1，独立报告；它不能代替其他P0。精确命名的原始要求保留在原测试清单，未删除或降低门槛。

## F01–F06 / O01–O08

| ID | 覆盖 | 最终状态 |
| --- | --- | --- |
| F01 | Store独占租约；第二宿主拒绝工作绑定且普通聊天可用；目录／旧历史哈希 | 最终CI待确认 |
| F02 | 生命周期恢复、初始化失败重试与退出后禁止重新开放 | 最终CI待确认 |
| F03 | 64KiB完整JSON、escaped日志、journal不截断、新call读取新状态 | 最终CI待确认 |
| F04 | 双端独立同步／后台时限；项目、Copy、有效设置、旧配置和重开 | 本地66/36；最终CI待确认 |
| F05 | 持久会话的草稿／队列／图片／问题／听写／模型／审批隔离 | 本地36；最终CI待确认 |
| F06 | PowerShellStartupTests、既有启动器与Job原生工厂共用bootstrap | 最终CI待确认 |
| O01–O02 | 先网络再角色再全量包推荐；无明确点击不下载 | WPF26 / Avalonia18专项；最终CI待确认 |
| O03–O04 | 非阻塞应用生命周期下载、同批去重、已知体积和可返回进度 | 共享86 / 双端UI；最终CI待确认 |
| O05–O06 | 缓存角色可用、最后选择在全量更新后热加载、失败保留与重试 | WPF95 / Avalonia25；最终CI待确认 |
| O07–O08 | 关闭引导不取消，退出应用停止；旧lesson ID和数字进度；不强迫同系列升级用户重做 | 共享及引导专项；最终CI待确认 |

## 最终提交和交付

- 前轮候选：`efdc0458c88888906795021ac4518540138afafd`；[Windows CI（测试时序失败）](https://github.com/Eason4869/deskpet/actions/runs/40041062448)、[macOS CI（双架构成功）](https://github.com/Eason4869/deskpet/actions/runs/40041062446)。测试修正后的完整结果见 [PR #30 检查](https://github.com/Eason4869/deskpet/pull/30/checks)，最终结果待确认。
- dev目标提交及远端标签v0.10.5-dev.2：待创建；不覆盖同名标签。
- 仅源码标签，不创建草稿或正式GitHub Release，不触发发布工作流。
- 独立JSON包：work/language-packs/DeskPet-0.10.5-dev.2-locales.zip，13个文件逐字节对照仓库，manifest包含各自SHA256。
