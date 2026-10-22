# 0.10.2-dev.3 图像工作流程验收

验收平台为 Windows、Windows PowerShell 5.1、.NET Framework WPF 和 Windows 上的 .NET 10 Avalonia Headless/Skia。Windows 上的 Avalonia 结果不能代表 macOS 真机。任务 1–8 的分项红／绿和审查已完成；本记录补充任务 9 的当前树回归、真实调用及打包范围。

文件 Agent 及其 `generate_image`、`ask_user` 和项目工具执行当前仅支持 Windows；macOS 的图像工作区、设置、任务管理、图库和图片历史不受此限制。共享集成测试在 Windows 保留实际 Agent 路径，在其他平台验证普通聊天回退，并在所有平台直接通过任务管理器及会话验证资产投递、跨会话拒绝、重启修复与零重复图片请求；这些检查不能作为完整 macOS 文件 Agent 的验收。

首轮真实 CI（`b3b924a`，macOS run `39739424146`）两种架构均暴露三处测试平台假设：普通聊天回退被误当作 Agent 图片请求未启动，以及打开的临时原图／隔离文件在 Unix 可成功 unlink，却被断言必须留存并报告清理警告。修正测试合同后，本机 .NET 10 完整共享回归 2,940 项通过，图片集成 60、管理 133、后端 68；Windows Framework 相关目标分别 60／133／68 项通过。保留 Windows 全部 Agent 与锁定重试断言，新增共享投递失败、外部会话拒绝和重启零重复请求覆盖。首次本机全量因新增夹具对合法 null 图片列表的断言错误退出 1，修正后重跑；沙箱 localhost 阻断的未完成尝试也保留。证据在 `work/dev3-verification/ci-fix-*`；本机结果不代表修复后原生 macOS CI 已通过。

同一 SHA 的 Windows run `39739424141` 另有三目标失败。本机 20 次关闭重开复现标题刷新回调空引用：后台关闭清空 workspace 的 Session/Queue 后，已排队的 UI 回调仍重复读取这些属性。改为持有原对象引用，排队及执行时均检查 workspace 已释放／应用正退出；回归仍保留 20 次关闭重开。会话状态标题测试用 220 ms 调度负载复现固定 160 ms 等待早于后台消息计时器的问题，改为按原消息 ID 有界等待真实状态并保留原文本与展开状态断言。图片动效夹具改为验证活动可见状态及系统动效偏好，保留禁用、最小化、隐藏和卸载断言；原 CI 没有记录系统动效开关，不能认定该主机必定开启了减少动画。本机三目标最终分别 9／82／95 项通过。相关生命周期定向回归见本地 CI 修复报告；没有重跑完整 Windows 目标矩阵。

第二轮 `d10bde7` 的 macOS 双架构 CI 全部通过，Windows 前述三目标也通过，仅 `AgentTerminalTests` 的 Unicode／退出码组合断言失败。原日志未记录实际终端结果，不能确定当时是超时还是输出分片。受控检查分别复现了 3 秒成功夹具预算触发正确超时，以及退出码 7、未超时时，读取块标签把中文拆成“中文[stdout] 测试”的真实输出问题。成功夹具预算单独调整为 20 秒，独立 1 秒超时和取消检查保留；带标签的原生输出改为按行／EOF 有界收集，标签与内容原子提交，每流缓冲及总捕获上限均为 256 Ki 字符。终端默认／延迟／分片各 14 项，原生命令 16 项通过，覆盖两种流的分片 Unicode、无换行末尾、取消／超时末尾保留、长行截断和进程树回收。相关工作流、审批、项目工具、沙箱和 MCP 检查全部通过；修改后的完整 Portable 重新运行，2,940 项通过。完整结果及原始失败保存在同一 CI 修复报告；新修复提交仍须最终 SHA 的 CI 验证。

`1af4eb7` 的 macOS ARM CI 全部通过；Intel 的共享核心与原生桥通过，Avalonia 图库模型搜索断言失败。夹具把一次 UI RunJobs 误当成后台查询已经完成。通过持有实际存储锁，复现“资产模型正确、后台查询未完成、UI 仍为旧 0 项”的同一断言；修复后先验证锁定期间查询保持异步，再释放锁，在原有 15 秒上限内等待原资产对应的真实 UI 页面，并输出搜索词、修订号、数量和原资产身份。初始、负向过滤与窄窗口截图准备也改为等待页面条件。仅修改测试，原模型搜索断言和过期查询保护保留；本机图片 UI 154、任务中心 52、清理／模型最终审查 8／4 项通过。新 SHA 的原生 CI 仍待主任务验证。

同一 `1af4eb7` 的 Windows run `39751307206` 中，137 个原生目标、编译、归档和登记通过，Avalonia 在等待待生成卡帧时钟时超时。同日志 WPF 夹具记录系统动画关闭；Avalonia 卡共享该 Windows 系统偏好，但夹具仍要求时钟必须启动。原 Avalonia 阶段没有记录全部卡状态，不能还原当时所有条件。受控复现仅打开卡自身的减少动画：卡已挂载、可见、任务为 Generating、文字为“正在生成图片”，时钟正确停止，旧等待仍触发相同超时。夹具改为等待这些真实就绪条件，并独立验证时钟是否符合实际系统偏好；减少动画下仍就绪的回归、应用／卡禁用、隐藏、最小化、卸载和恢复检查均保留，15 秒上限及系统设置不变。增加 Avalonia 自身的完整状态诊断，仅修改测试。本机图片 UI 159、任务中心 52、完整 Avalonia 2,277 项通过，覆盖原 CI 超时后未到达的后半段；最终 SHA 的原生 CI 仍须主任务确认。

## 本地自动化

运行记录保留在忽略的 `work/dev3-*` 中，不包含服务凭据。测试使用隔离数据目录，不修改日常数据、设置或 dev.2 证据。

| 范围 | 当前证据 |
| --- | --- |
| 默认入口和 CI 登记 | `python tests/ImageWorkflowRegistrationTests.py`：首次 4 项失败，补齐后 4/4 通过 |
| WPF 图片界面 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target ImageGenerationUiTests`：94 断言通过 |
| Avalonia 图片界面 | `dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --image-ui-only`：153 断言通过 |
| Windows 完整回归 | 156 个独立目标首轮 153 通过、3 失败；原始结果为 `work/dev3-windows-full-initial-complete-results.json`。三项后续复核及相关 9 个目标全部通过，见下文，保留首轮失败 |
| 共享核心完整回归 | 完整默认运行 2,931 项通过，JSON 汇总 `work/dev3-portable-report.json` success=true／failures=[]，日志 `work/dev3-portable-full.txt` |
| Avalonia 完整回归及三条退出路径 | 默认全套 2,241 项通过；三个独立退出进程 writer 3／held 3／graceful 2，通过。首轮外观导入目录移动失败保留，同 DLL 重跑通过，见下文 |

Windows 矩阵从 `.github/workflows/check.yml` 的目标列表开始，再补充 `Build.ps1` 中其余 `*Tests`，排除两个显式付费实时目标。它覆盖任务存储、调度、动效、多图、图库管理与导出、两端集成，以及既有六协议、布局、History、Queue、Agent、权限、问题交互、附件预览、主题和生命周期。每个目标串行启动 Windows PowerShell 5.1，保存退出码、耗时及完整输出。问题交互与其他 UI Smoke 使用既有目标；`PredictionTests` 保留 `--ui` 参数。

完整共享核心和 Avalonia 命令如下；构建串行执行，不与同一测试 apphost 重叠。localhost 服务夹具在允许本机网络的执行环境运行，不能将沙箱禁止连接误写为服务兼容失败。

```powershell
dotnet build desktop/DeskPet.Tests/DeskPet.Tests.csproj -c Release -m:1 --no-restore
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --report-json work/dev3-portable-report.json
dotnet build desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj -c Release -m:1 --no-restore
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --image-writer-exit-ui-only
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --image-held-exit-ui-only
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --image-exit-ui-only
```

共享核心完整入口新增 Motion、Group、Management 和 Export，不再仅支持专用参数；命名汇总同时统计本版任务中心、任务存储、调度、集成等套件。Avalonia 默认入口新增任务中心；真正退出会结束应用生命周期，因此 writer、held finalizer 和 graceful 三个检查各自以新进程运行。Windows 与 macOS CI 均登记这些进程，CI 不调用真实图片服务。


全量默认 Avalonia 首次在既有 `SpriteAssets.Import` 的临时外观目录移动发生 `IOException`（Access denied），尚未进入新图片套件。保留 `work/dev3-avalonia-full.txt`；读取导入与图片流释放代码后，没有修改产品或放宽校验，运行同一已构建 DLL 再次完整通过 2,241 项，日志 `work/dev3-avalonia-full-rerun.txt`。首轮失败原因尚未证明，不归因为已知系统／防病毒问题。三条真正退出路径独立进程 3／3／2 项通过，日志为 `work/dev3-avalonia-writer-exit.txt`、`work/dev3-avalonia-held-exit.txt`、`work/dev3-avalonia-exit.txt`。

快速草稿修复后的 Windows 相关回归分别为工作区管理 9、普通草稿 9、差异预览 3、工作区 4、会话归属 14、归属界面 15、图片生命周期 47、WPF 任务中心 55 和集成 53 项，9 个目标退出码均为 0；逐项目志 `work/dev3-final-focused-<target>.txt`。最终 Windows 证据是完整首轮与修复后的相关目标回归，不声称重新运行了第二次完整 156 目标矩阵。版本变更后单独运行 ReleaseVersion、ReleaseIdentity 与 UpdateChannel 检查。

## 本轮发现与修复

- 两端 40 张 2048×2048 图片的实际图库操作首次失败：选择会替换所有卡片、重复解码并丢失键盘焦点。日志为 `work/dev3-ui-red.txt`、`work/dev3-ava-ui-red.txt`。改为原位更新选择浮层、工具栏和计数，保留过滤与资产变化时的原有查询路径。最终断言保留 40 个卡片与 Bitmap 引用、键盘焦点；WPF 一次观测约 209 ms，包含固定 200 ms 等待，不作为设备性能基准。
- WPF “图片详情”Expander 标题首次主题断言失败，日志为 `work/dev3-ui-theme-red.txt`；使用现有 `Ui.Ink` 主题前景色，浅深主题均验证。查看最终截图确认深色标题可读。
- 入口登记首次 4/4 失败，修复后 4/4 通过；没有删除或跳过既有测试。
- 全量回归发现未保存的快速对话展开被严格目录校验拒绝。新增共享回归先红，UI 打开路径在 owner 锁内仅返回当前已存在且未关闭的草稿；图片修复仍需原目录成员资格，不重建正在关闭或已关闭草稿。原 QuickExpansion 界面检查与新增 canonical/no-persist/repair-reject/closing/closed 检查均通过。
- 原 `PetRuntimeTests` 首轮无法命中实际宠物像素；在全部目标结束后运行同一可执行文件，22 项通过。未修改产品或断言，不把一次通过当作已证明的 DPI／环境原因。
- `AgentChangePreviewTests` 夹具曾用工具创建初始文件，实际合同是相对任务开始前原备份／不存在状态的累计差异。改为在工具编辑前直接创建初始文件，保留 +1/-1 与逐行断言，3 项通过；原创建文件累计基线由既有 `AgentChangesTests` 保留。未修改产品差异语义。

浅深主题、390/430 DIP 窄窗口、宽窗口、点阵固定相位、多图状态混合、隐藏／最小化／卸载停止时钟、动效开关、可访问的选择浮层均有自动化 UI 证据。截图是合成夹具，不能标成真实服务作品；时钟断言证明停止重绘，没有进行操作系统级 CPU/功耗测量。

## 真实服务：请求账本与保存资产

所有请求使用受保护配置中的 `gpt-image-2.5-sunburst`，无自动重试、压力请求或 Gemini 扫描。配置保持 Windows CurrentUser DPAPI 加密，通过既有连接设置只在内存解密，未复制明文密钥或修改生产设置。聊天协议由本地脚本驱动真正内置 `generate_image`，没有请求文字模型；图片 API 为真实网络调用。

| 阶段 | 生成调用尝试 | 结果 |
| --- | --- | --- |
| 初始页面＋首个工具同时排队 | 2 | 两项失败；TCP 预检成功，但初版夹具只保留 `ApiException` 和通用任务错误，具体 HTTP／传输原因无法追溯 |
| 最后一次单页面诊断 | 1 | `POST /v1/images/generations`、鉴权头存在，HTTP 200，JSON 2,437,298 字节；原图提交且实际原生预览成功 |
| 控制器明确扩展的两个新内置工具调用 | 2 | 同一原始回复内两次 HTTP 200，JSON 2,488,134／2,333,902 字节；两任务 ID、两资产保留在同一消息 |
| 导出、回收、恢复、切图及独立进程重开 | 0 | 使用同一批保存资产、拒绝 HTTP 的执行器，最后 14 项 UI／资产检查和独立进程 3 项恢复检查通过 |

累计 **5 次显式生成调用尝试：初始 2 次失败，后续 3 次成功，保存 3 张原图**。最初控制器设上限 3；单图诊断成功且零 HTTP 复用通过后，控制器明确将上限扩到 5，只允许同一脚本回复中的两个新工具调用。没有重新运行三调用或单页面付费模式；达到 5 后没有再生成。额外排队取消项在首个请求转发前确定取消，发送 0 次 HTTP。

初始失败保留在 `work/dev3-live-6f5005886a8f409892d3909387efad55/failure.json` 与 `work/dev3-live-result.txt`，不将 `ApiException` 当作具体 HTTP 状态，也不将后续成功当作初次失败原因已知。控制器只读 `/models` 返回 HTTP 200、35 个模型，两配置模型均存在，生成数 0；证据为 `work/dev3-verification/service-models-diagnosis.json`。

成功资产统一保存在 `work/dev3-live-4fa68adf8726418d8ae1bb482be0323e`：

- 三张均为 1536×1024；原始字节分别 1,827,576（页面）、1,865,701 和 1,750,029（工具）。
- `http-metadata.jsonl` 为页面诊断，`conversation-http-metadata.jsonl` 为两工具；只记录方法、无查询路径、鉴权头是否存在、协议／模型、状态／类型／长度或异常类型，不记录头值、密钥或原始响应正文。
- 页面诊断后的初始导出因夹具未创建目的目录失败；保留 `failure.json`，创建真实存在的选择目录后以零 HTTP 复用完成。未改变导出 API 对目录的校验。
- 付费对话检查在第一次预览后立即读取下一张尚未附加到视觉树的卡片而失败；保留 `conversation-failure.json`。零 HTTP 重载同一加密历史，等待真实卡片、解码和布局在 5 秒内就绪，完整验证两张切换与预览。未修改产品。
- `conversation-reuse-report.json`、`conversation-reopen-report.json` 与 `work/dev3-live-conversation-final-reuse.txt`／`work/dev3-live-conversation-final-reopen.txt` 记录 14／3 项通过，均为 HTTP 0。重开在独立新进程加载同三资产、三成功任务与原两图回复，不重发生成。
- 四张实际 WPF 截图为 `conversation-wide-slot-1.png`、`conversation-narrow-slot-1.png`、`conversation-wide-slot-2.png`、`conversation-narrow-slot-2.png`。分别为 760／390 DIP 和两种选图，宽布局右侧、窄布局底部缩略图；已查看真实蓝杯、黄杯及选中描边。
- 原图导出逐项验证与资产字节完全一致；重名避免覆盖；同三资产移入回收站后原图仍可读取，恢复保持 ID。

可复核命令（付费命令仅记录本次执行，不应自动再次运行）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target ImageWorkflowLiveTests -TestArguments --compile-only
& .\work\ImageWorkflowLiveTests.exe --run work/live-image/service.json
& .\work\ImageWorkflowLiveTests.exe --single-manual work/live-image/service.json
& .\work\ImageWorkflowLiveTests.exe --conversation-only work/live-image/service.json work/dev3-live-4fa68adf8726418d8ae1bb482be0323e
# 以下仅复用保存资产，HTTP 被拒绝：
& .\work\ImageWorkflowLiveTests.exe --conversation-reuse-only work/dev3-live-4fa68adf8726418d8ae1bb482be0323e
& .\work\ImageWorkflowLiveTests.exe --reopen-only work/dev3-live-4fa68adf8726418d8ae1bb482be0323e
```

## 构建、打包与发布边界

本地行为验收结束后将显示版本改为 `0.10.2-dev.3`，数字版本保持 `0.10.2.0`。最终提交的干净源码构建命令为 `python work/dev3-clean-preflight.py`；实际 40 位 HEAD、输出目录、身份与 SHA256 结果由 `work/dev3-clean-latest.json` 指向的独立报告记录，避免在源码内写入会改变自身的提交哈希。应用及更新助手由既有打包验证器核验完整 InformationalVersion，必须精确等于版本＋该 HEAD。

打包使用显式、独立的 `work/dev3-*` 输出路径，包含 Windows ZIP、源码、独立应用、完整角色与 v2 模板，并核验原始字节、SHA256 清单、排除本地设置和凭据。`Build.ps1` 保持 UTF-8 BOM，使用 Windows PowerShell 5.1。

本机未发现 Inno ISCC 编译器，因此不声称本地安装器通过。Windows 检查工作流增加创建标签前的实际 `BuildInstaller.ps1 -OutputDirectory work/dev3-ci-package`；发布工作流的不可变标签守卫保持原样，不为打包提前创建标签。本任务未推送、检查远端 CI、创建标签或 Release；最终由控制器先推 dev、处理实际 CI，CI 通过后发布。`v0.10.2-dev.2` 保持原提交。

## 明确未执行

- macOS arm64/x86_64 原生平台 CI 由后续远端流程验证；当前本地没有执行，也没有预先修复旧 CI 日志中的 Unix 文件锁假设。
- macOS 真机预览、保存对话框与系统减少动画探测未验收。应用内动效开关可用；macOS 原生减少动画探测尚未实现。
- Windows 手工长期使用与系统级后台 CPU/功耗测量未执行。
- 本地安装器因缺少编译器未构建；最终远端 CI、安装器、不可变标签和预览发布不得由本地结果代替。


## 最终整分支审查修复补充（2026-10-22）

本次仅处理最终审查的三项 Important：恢复出的成功对话任务从 `Pending` 转为追加的 `AwaitingSync` 状态，显示等待同步并提供原资产修复；当前进程的 `Pending` 仍由原写入者负责，独立重试保持 `Detached`。两端任务中心根据资产索引更新弱结果可用性，永久删除后保留任务成功状态、显示“图片已删除”并移除查看操作；资产事件随视图生命周期退订。服务／模型选项消失导致有效筛选改变时，清空选择并发起新的修订查询，不渲染旧筛选结果。

新增的四组原生用例通过 `--final-review=recovery|purge|service|model` 分别捕获行为 RED，并已进入两端既有默认任务中心测试入口。覆盖 Generating／Saving／Succeeded 三个恢复窗口、原历史已附图但交付日志未提交、重复同步只附图一次且执行器调用为零、活动 Pending／Detached 不出现修复、可安全永久删除的成功弱结果、可见更新／隐藏退订／重新打开／任务恢复，以及服务和模型最后一项删除后的 All 数量、页面、清空选择和过期修订。

本次定向 GREEN（不重跑完整 156 目标、Portable 2931 或 Avalonia 2241）：

| 检查 | 本次结果 | 日志 |
| --- | --- | --- |
| WPF 默认任务中心（含已有退出与修复归属、新增四组） | 104 passed | `work/final-fix-wpf-task-ui-final.log` |
| WPF 任务存储／调度／中心生命周期／图库管理／图片集成 | 25／43／47／132／53 passed | `work/final-fix-wpf-<目标名>.log` |
| WPF 图像／图库原生 UI | 94 passed | `work/final-fix-wpf-ImageGenerationUiTests.log` |
| .NET 10 上述五组共享检查 | 25／43／47／132／53 passed | `work/final-fix-portable--image-*.log` |
| Avalonia 默认任务中心（含新增四组） | 52 assertions | `work/final-fix-ava-task-ui-final.log` |
| Avalonia 图像／图库原生 UI | 153 assertions | `work/final-fix-ava-gallery-ui-final.log` |

图库原生 UI 原有五秒等待在修复代码和原始 HEAD 上均失败，诊断均为正确 40 张但只有 28 张解码、任务中心未订阅。保留原失败及同一二进制重跑日志 `work/final-fix-ava-gallery-ui-green.log`、`work/final-fix-ava-gallery-ui-rerun.log`，对照日志为 `work/final-fix-ava-gallery-baseline.log`。临时 20 秒诊断测得原始 HEAD 完整解码耗时 7045 ms，修复代码 7083 ms，两者均通过原有 153 项断言；日志 `work/final-fix-ava-readiness-baseline.log`、`work/final-fix-ava-readiness-fix.log`。据此仅给四十张 2048×2048 测试图的两处等待显式 15 秒预算，其余等待仍默认五秒；图片数量、非空解码、控件／位图复用和焦点断言均保留。这是有界测试等待预算调整，不是产品性能保证；失败最多晚十秒报告。未保留临时诊断代码。

全部 fixtures 隔离，未读取受保护的真实服务配置；真实生图累计仍为 5/5，本次没有增加真实 HTTP。版本与 README 三项未完成路线图保持原样。最终干净提交的三项二进制身份及九项打包清单由 `work/dev3-clean-latest.json` 指向的新报告记录；旧提交报告仍是历史证据。本次没有执行本地 ISCC、macOS 真机验收、远端 CI、推送、标签或发布，原有未执行项继续适用。
