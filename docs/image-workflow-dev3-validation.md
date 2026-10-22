# 0.10.2-dev.3 图像工作流程验收

验收平台为 Windows、Windows PowerShell 5.1、.NET Framework WPF 和 Windows 上的 .NET 10 Avalonia Headless/Skia。Windows 上的 Avalonia 结果不能代表 macOS 真机。任务 1–8 的分项红／绿和审查已完成；本记录补充任务 9 的当前树回归、真实调用及打包范围。

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
