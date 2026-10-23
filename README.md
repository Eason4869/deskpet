[简体中文](README.md) | [English](README.en.md)

<div align="center">
  <picture>
    <source media="(max-width: 640px)" srcset="previews/readme-hero-mobile.svg">
    <img src="previews/readme-hero.svg" width="1200" alt="桌面伙伴 DeskPet：让桌面多一个伙伴，E包、大肥鱼、马尔芙与小贝陪你聊天和工作">
  </picture>
  <h1>桌面伙伴 · DeskPet</h1>
  <p>原画互动 · 独立人格 · 项目工作 · 听写与朗读</p>
  <p><a href="#下载安装">下载安装</a> · <a href="#开始使用">开始使用</a> · <a href="docs/configuration-guide.md">配置引导</a> · <a href="CHANGELOG.md">更新日志</a></p>
</div>

DeskPet 是一个住在桌面上的伙伴。你可以让它散步、看向鼠标，摸摸头、投喂；接入自己的模型服务后，还能聊日常、讨论想法，围绕项目一起处理文件。

当前开发目标为 **0.10.5-dev.2**：在已验收的语言 JSON 重构基础上，补齐多语言并开发 Agent 后台任务。语言分支支持 13 种语言和“跟随系统”，头像菜单的“语言”入口位于“设置”上方；保存后在下次启动生效，保留手动选择，未适配的系统语言使用 English。默认人格和请求的回复语言随实际生效语言调整，自定义内容保留。

语言名称使用本名：简体中文、繁體中文、English、日本語、한국어、Français (France)、Deutsch (Deutschland)、हिन्दी (भारत)、Bahasa Indonesia、Italiano (Italia)、Português (Brasil)、Español (Latinoamérica)、Español (España)。

后台任务已完成核心、原生进程和工具/会话阶段；Windows WPF 已接入应用级任务服务、项目操作保护、退出清理、原回复任务卡片、任务列表与纯文本日志。预览地址只来自本地回环日志，点击后才在浏览器打开。Avalonia 已加入最小持久工作会话选择、双窗口复用与来源隔离；后台任务界面、退出验收、首次引导和总验收仍在开发，尚未整体交付。进度见 [实施记录](docs/agent-background-jobs/implementation-progress.md)，文案维护见 [资源指南](docs/localization/zh-CN-resource-guide.md)。完成验收后合入 `dev` 并打源码标签，暂不发布 Release；下方下载链接仍指向已发布的正式版本。

**当前正式版本：[0.10.4](https://github.com/Eason4869/deskpet/releases/tag/v0.10.4)**。提供 Windows 安装／便携包、macOS Apple Silicon／Intel 双架构预览、完整源码、五位官方伙伴的完整高清角色包和创作模板。macOS 的界面与能力范围见 [平台与升级](#平台与升级)，本页应用截图以 Windows 为准。

0.10.4 修复重启后切回 E包：本地角色包有效时，离线恢复退出前选中的伙伴。0.10.3 的功能继续保留：五角色万圣节新装、悬停彩蛋收藏与节日问候；共享生图队列、任务中心、对话多图及图库管理；技能仓库发现、局域网中转和会话草稿；统一深色设置与对话内操作确认。应用只内置 E包的完整高清素材，其他官方伙伴首次选择时按需下载，也可在设置中全量下载／更新。详见 [节日服饰](docs/holiday-outfits.md)、[图像生成](docs/image-generation.md)、[角色包说明](docs/character-packs.md) 与 [更新日志](CHANGELOG.md)。

历史开发版本及源码快照保留在更新日志中；正式版与预览版通道均可更新到更高的 0.10.4 正式版。

在“关于”页选择更新通道，切换只保存偏好，不会降级。点击“立即更新并重启”可跳过剩余 5 秒等待，仍会保护运行任务和未保存内容。制作新角色的模板独立下载；0.10.3-dev.1 起，非默认伙伴使用独立的完整高清角色包，下载沿用应用更新的 GitHub 加速方式，具体规则见 [发布与升级说明](docs/release-naming.md)。

## 先认识你的伙伴

<picture>
  <source media="(max-width: 640px)" srcset="previews/readme-interaction-mobile.svg">
  <img src="previews/readme-interaction.svg" width="1200" alt="两位伙伴的原画、完整动作轮盘和快速对话截图；互动无需AI，聊天需要配置模型">
</picture>

界面支持浅色、暗色与跟随系统，可在 **设置 → 通用 → 视觉风格** 点击缩略卡切换；选择自动保存，无需重启。

五位官方伙伴均可选择，也支持导入自制外观。首次启动默认 **E包**；其余伙伴首次选择时会下载并安装完整高清角色包，成功后切换。首次引导只提醒下载需求，完成引导后再实际选择。每位伙伴有各自的普通聊天记录、人格和音色偏好。

| 伙伴 | 认识一下 |
| --- | --- |
| **E包** | 白发蓝眸、白蓝运动服，傲娇又毒舌的小伙伴 |
| **大肥鱼** | 蓝白配色的鲸鱼娘，温和又俏皮 |
| **马尔芙** | 金发绿眼的魔法少女，聪明又俏皮 |
| **小贝** | 黑白青色的科技少年，机灵又温柔 |
| **咕嘎** | 黑发灰蓝眼的企鹅服女孩，呆萌好奇、贪吃，有一点小脾气 |

支持[完整角色资源包](docs/character-packs.md)：直接导入 ZIP 更新头像、人格、双套动作和音色推荐，同 ID 只展示一项并保留个人记录。设置中的“全量下载／更新”可一次安装或更新全部官方完整包；已解锁服饰在当前伙伴的“衣柜”中选择，保留个人记录。

1. **拖动、单击和抚摸**：换个位置，触发伙伴的互动。锁定位置后保持原位。
2. **鼠标中键**：打开动作轮盘，选择小憩、投喂或招牌动作。轮盘与右键里的跑动会实际移动一小段，到屏幕边缘停止；锁定时原地演示。
3. **双击伙伴**：打开快速对话；**右键伙伴**或**双击托盘图标**可打开完整对话。

伙伴思考或工作时仍能互动。开始拖动、暂停或切换动作，会停止手动跑动。**桌宠互动不需要 AI 配置；文字聊天、听写、朗读与 Live 需要相应模型服务。**

## 一个窗口，陪你聊天和工作

<picture>
  <source media="(max-width: 640px)" srcset="previews/readme-workspace-mobile.svg">
  <img src="previews/readme-workspace.svg" width="1200" alt="Windows 0.10.0 统一主窗口完整截图：左侧功能入口、六条示例会话、聊天正文及完整输入区">
</picture>

<sub>主窗口示例来自 Windows 0.10.0，保留完整窗口；会话列表和聊天内容为隔离的本地示例，不含个人记录，未请求模型服务。可点击查看 [主窗口原图](previews/workspace-0.10.0.png)、[动作轮盘](previews/action-palette.png) 和 [快速对话](previews/quick-chat.png)。</sub>

### 从一句话，到一个项目

| 场景 | 怎么用 |
| --- | --- |
| **快速对话** | 双击伙伴，在头顶聊两句。重新打开会开始新话题，已有记录保留在当前伙伴的“快速对话”分组。 |
| **聊天** | 使用伙伴的人格，完整窗口聊天保存在“默认分区”或自选聊天分区，分别保留上下文与草稿。 |
| **工作** | 绑定项目目录，读取、搜索和修改文件，查看实际执行记录。需要支持原生工具调用的文字模型。 |

聊天分区直接管理会话，工作分区管理项目及下属会话。两种模式均支持新建、重命名分区；会话点击后保持原位，可拖动排序并保存，也可将工作会话拖入其他项目、普通聊天拖入其他聊天分区。项目和对话支持置顶、归档与分组。切换页面或会话保留草稿与阅读位置；侧栏可用顶栏按钮或 **Ctrl + Shift + S** 收起。

### 写得顺手，看得完整

输入区支持 Markdown 标题、列表、引用、代码和表格；在 **工作设置 → 编辑器** 可切换纯文本模式。默认 **Enter 发送、Shift + Enter 换行**。

通过 **+**、粘贴或拖放添加图片、文件与文件夹引用，Windows 还支持应用窗口快照。上传与返回的图片可在窗口内放大、缩放和拖动查看；按 **Esc** 或点击空白退出。回复支持复制、朗读、重新生成和导出。

听写将语音变成草稿，自动朗读可在对话顶部切换，Live 用于实时语音交流。**预测消息**会额外请求文字模型，可在编辑器设置中关闭；接受建议后仍需确认发送。运行中追加要求可选择 **排队** 或 **引导**，后者在下一处可调整的请求边界接入，不会撤销已经发出的请求或完成的修改。

### 工具就在对话旁边

本分支新增 **图像 → 生成 / 图库 / 任务**：配置独立生图模型后，可上传参考图生成，按瀑布流或网格浏览缩略图，点击查看详情、预览与另存。Agent 支持直接在对话反馈图片，并增加询问用户、检查本地图片和 Git 只读工具。详见 [使用说明](docs/image-generation.md)。

### 0.10.2-dev.3 已实现

- [x] **生图动效**：页面与对话共用圆角点阵光斑动效，展示真实阶段、耗时及取消状态。
- [x] **图库管理**：收藏、多选、批量原图导出、按服务、模型、来源和时间筛选，以及受引用保护的回收站与清理。
- [x] **生图任务中心**：统一任务队列、并发限制、完成通知、失败原因、手动重试及重启状态恢复。
- [x] **对话多图切换**：左侧大图、右侧可点击缩略图，窄窗口适配，保留同组图片和生成状态。

使用方法、保存与恢复及平台限制见 [图像生成说明](docs/image-generation.md)。

通过标签栏 **+** 或 **Ctrl + Shift + B** 新建浏览器、文件预览、PowerShell 终端、临时侧边聊天或文件变更页。支持聊天与工具分屏，**Ctrl + Shift + F** 切换完整标签视图，窗口变窄时自动适配。

文件卡片可预览 HTML、Markdown 和带行号的源码，也可系统打开、定位、复制路径或另存为。HTML 外部资源默认关闭；内置浏览器暂不支持下载。临时侧边聊天不读取主聊天上下文与项目文件，关闭后不保存。

主聊天固定在首个标签；隐藏工具区或切换布局保留工具状态，关闭标签才释放实例。手动终端与 Agent 的终端开关、审批独立管理，沿用打开时的项目与沙箱设置，关闭后结束对应进程树。

## 下载安装

Windows 支持 **Windows 10／11 x64、.NET Framework 4.8+**；macOS 预览需要 **macOS 14+**。请按系统和芯片架构选择附件，完整版本说明与校验文件见 [v0.10.4 发布页](https://github.com/Eason4869/deskpet/releases/tag/v0.10.4)。

| 平台 | 下载 |
| --- | --- |
| **Windows 安装版 · 推荐** | [DeskPet-Windows-Setup-0.10.4.exe](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Windows-Setup-0.10.4.exe) |
| Windows 便携版 | [DeskPet-Windows-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Windows-0.10.4.zip) |
| Mac Apple Silicon · 预览 | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-arm64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-arm64-preview.zip) |
| Mac Intel · 预览 | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-x86_64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-x86_64-preview.zip) |

<details>
<summary>源码、外观包、创作模板与 SHA256</summary>

| 附件 | 下载 |
| --- | --- |
| 完整源码 | [DeskPet-Source-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Source-0.10.4.zip) |
| 完整角色包 | [E包](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-e-bao-0.10.4.zip) · [大肥鱼](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-dafeiyu-0.10.4.zip) · [马尔芙](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-marfu-0.10.4.zip) · [小贝](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-xiaobei-0.10.4.zip) · [咕嘎](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-gugugaga-0.10.4.zip) |
| 官方角色目录 | [DeskPet-Companions-0.10.4.json](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companions-0.10.4.json) |
| 外观创作模板 | [DeskPet-Skin-Template-v2-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Skin-Template-v2-0.10.4.zip) |
| 附件校验 | [SHA256.txt](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/SHA256.txt) |

</details>

## 开始使用

1. **安装或解压**：运行 Windows 安装程序，或将便携 ZIP 完整解压到可写目录，再运行 `桌面伙伴.exe`。不要在压缩包内直接启动。
2. **选一位伙伴**：先试试拖动、抚摸与动作轮盘；只想使用桌宠，可以跳过 AI 配置。
3. **配置模型**：打开 **设置 → 配置引导**，按需填写文字模型、听写、朗读或 Live 的服务地址、协议、密钥和模型，再主动测试连接。
4. **开始聊天或工作**：双击伙伴快速聊天，或打开完整对话管理上下文、项目和文件。

配置引导支持跳过本步／本章、稍后继续和保留进度；只有主动点击测试才会发送测试请求、播放声音或开启麦克风。首次安装或进入更高主／次版本系列会自动展示一次，同系列补丁不重复，也可在设置中手动重开。

随包提供的文档可离线阅读：[安装、升级与备份](docs/installation.md) · [配置与使用引导](docs/configuration-guide.md) · [模型与语音配置](docs/dialogue-setup.md)。离线查看说明不代表远程模型可离线调用。

## 工作权限，由你掌握

Windows 内置文件 Agent，无需另装 Agent 命令行工具。可查看文件净变更、执行输出、审批与子任务记录；文字模型需支持 OpenAI 或 Anthropic 原生工具调用。

| 会话权限 | 行为 |
| --- | --- |
| **请求批准** | 项目内常规文件操作可直接执行，越界访问、联网等例外交给你确认；Agent 终端命令逐条人工批准。 |
| **帮我批准** | 新配置的默认值，符合条件的操作交给独立审核模型，无法确认或审核失败时转人工；Agent 终端命令仍逐条人工批准。 |
| **完全访问权限** | 经确认后扩大到当前账户可访问的文件范围并关闭工作区沙箱，不会获得管理员权限。 |

Agent 终端默认关闭。启用工作区沙箱时，Windows 通过 **AppContainer** 隔离终端及其子进程，限制目录访问并默认阻止联网；隔离启动失败会停止执行。**开启沙箱不免除“请求批准／帮我批准”下的终端命令确认。** 文件和网页工具由宿主按权限执行，MCP 不在终端沙箱内；MCP 与 GitHub 写入仍独立确认。

计划模式在工具层禁止写入。默认修改前备份，恢复时核对后续改动；只读子任务继承父任务的限制。项目事实由你手动维护，不将模型结论自动当作执行证据。详见 [工作、互动与工具使用](docs/agent-workflows.md) 和 [文件 Agent](docs/conversation-agent.md)。

## 扩展、用量和个人设置

| 入口 | 能做什么 |
| --- | --- |
| **插件 → 技能** | 添加公开 GitHub 仓库后搜索、预览和安装，也支持 ZIP 与本机文件夹。按需加载，支持启停与移除；安装保留来源，不自动运行脚本。 |
| **插件 → MCP** | 添加 stdio 或 HTTP 服务器，保存后主动连接，不随应用启动自动运行。HTTP 目前为 JSON 子集，暂不支持 SSE。 |
| **用量统计** | 查看请求次数、Token、全年热力图、7／30 天历史和会话排行，按服务商、模型、项目与功能筛选。 |
| **设置** | 搜索与调整伙伴、模型、工作、通用选项；支持大小、置顶、锁定、鼠标穿透、全屏隐藏、安静模式和快捷键。 |

用量包含新采集的聊天、工作、子任务、审核、预测、标题、压缩和问候等文字请求，暂不含 ASR、TTS 与 Live。实值、估算、未知和真实零分别标记；账本不保存提示词、回复正文或密钥，不等同于服务商账单或套餐余额。

个人设置、密钥、记录与导入内容保存在本机数据目录，迁移或覆盖升级前请备份完整 `data` 文件夹。文件、图片与工具读取结果可能发送给你选择的模型服务，多轮工作按服务商规则计费。自制伙伴见 [外观格式](docs/skin-format.md) 与 [互动扩展](docs/interaction-extension.md)。

## 平台与升级

| 平台 | 当前边界 |
| --- | --- |
| **Windows** | 安装版与便携版，包含本页展示的统一主窗口、桌宠互动、文件 Agent、工具页与 AppContainer 终端隔离。 |
| **macOS 预览** | Apple Silicon／Intel 双架构，共享部分核心，拥有独立界面。Windows 的统一窗口、配置引导与文件 Agent 能力不代表已在 Mac 实现，请以预览发布说明为准。 |

Windows 可在 **关于与更新** 选择正式版或预览版通道，在安全空闲时校验并安装，始终禁止降级。旧客户端先升级到 0.10.0，才能接收之后公开发布的更高 dev 版本；仅有源码标签不会成为更新候选。**0.5.4 及更早安装版**需先手动安装 0.5.6，或直接手动安装新版；便携 ZIP 不受安装包改名影响。开发中的改动见 [dev 分支](https://github.com/Eason4869/deskpet/tree/dev)。

Windows 安装包目前未做应用代码签名；Mac 预览为 ad-hoc 签名，未经 Apple Developer ID 公证。安装与备份说明见 [安装指南](docs/installation.md)，素材来源、协议及第三方许可见 [素材与平台说明](docs/sources.md) 和 [第三方声明](docs/third-party-notices.md)。

## 后续计划

- [ ] 应用连接：外部应用的授权、连接状态与可用能力管理。
- [ ] 插件包运行时：包导入、清单与依赖校验、启停及生命周期管理。

---

作者：**彧晟Eason** · [问题反馈](https://github.com/Eason4869/deskpet/issues) · [更新日志](CHANGELOG.md) · [配置文档](docs/configuration-guide.md) · [素材与许可](docs/sources.md)
