# 图像生成与项目工具验收（2026-10-22）

## 基线与实现范围

基于远端不可变标签 `v0.10.2-dev.1`，提交 `c4ac7a7ed3e894b88283901d483cbec6c6c4707c`，在 `codex/image-agent-tools-v0.10.2` 分支实现。随后按用户要求合入远端 dev 的构建与回归修复，版本更新为 `0.10.2-dev.2`，沿用 dev 开发快照渠道。Windows 使用现有 C# 5 / .NET Framework WPF 构建；共享核心及 Avalonia 使用 .NET 10。

完成独立生图配置、六类服务商预设与对应协议、手动生成/参考图、图库两种缩略图排列和点击详情、对话图片工具与内嵌卡片、独立资产和历史恢复；完成首批 `ask_user`、`inspect_image`、Git 三种只读工具。后续可选模块不计入本批实现。

## 自动化验证

| 目标 | 结果 | 覆盖重点 |
| --- | --- | --- |
| `ImageGenerationBackendTests` | Windows / Portable 各 67 通过 | 独立配置、加密、Base64/URL、编辑、50 MB、损坏响应、取消/超时、403/429、原子提交及恢复、临时资产转持久 |
| `ImageProviderProtocolTests` | Windows / Portable 各 85 通过 | 六预设、原生请求头/路径/JSON、参考图、参数省略/校验、安全响应、MiniMax 业务错误、Seedream 各模型字段差异 |
| `ImageGalleryLayoutTests` | Windows / Portable 各 32 通过 | 原始宽高比、方形网格、窄窗口、排列稳定、有限几何及无重叠 |
| `ImageGenerationIntegrationTests` | Windows 24 通过 | 实际 Agent 工具路径、原消息归属、引导竞态、历史/重启、临时提升、失败状态及不重试 |
| `ImageGenerationUiTests` | Windows 47 通过 | WPF 导航、两种排列、详情、取消、图片卡片、六预设、添加界面与问题窗口生命周期 |
| Avalonia `--image-ui-only` | 100 通过 | 同类界面行为、问题交互及浅深/窄窗口截图 |
| `AgentProjectToolsTests` | 14 通过 | 问题恢复/取消、视觉输入、只读 Git、路径边界、输出限额和进程树停止 |
| `AgentProtocolTests` | 26 通过 | OpenAI/Anthropic 工具循环与图像内容块，持久记录不含 Base64 |
| `AgentQuestionUiSmoke` | 14 通过 | 问题选项、自由回答、原任务绑定与取消 |

已有功能定向回归：History 21、Provider 26、Conversation 12、Queue 52、ChatImagePreview 46、AppShell 74、Theme 32 全部通过。新增目标已加入 Windows CI，真实服务测试不自动进入 CI。

共享核心完整回归发现 Queue 测试留下明文测试保护器，导致后续图片索引加密断言失败；测试运行器现于各套件前恢复独立的加密夹具，不修改生产保护行为。修复后 .NET 10 共享核心完整回归 **581 项通过、0 失败**。

本地证据保存在忽略的 `work/image-agent-regression`、`work/image-ui-evidence`；Avalonia 截图在桌面测试输出的 `artifacts` 下。界面截图使用隔离夹具及真实生成的杯子图片，不包含用户密钥。

## 真实服务验证

### 0.10.2-dev.2 合并复核

合入远端 dev 的 `30a7a57`、`608ddda` 后，保留 `Build.ps1` 的 UTF-8 BOM，并在 Windows PowerShell 5.1 下重新执行 9 组定向验证：版本、构建身份、更新通道、图片协议 85、图片后端 67、图片集成 24、Agent 协议 26、侧栏分区 7、图片界面 47，全部通过。共享核心完整回归 581、Avalonia 图片界面 100、Python 附件命名 4、运行包规则 3、发布守卫 1 也通过。没有重复调用真实生图服务。

## 真实服务验证结果

用户授权的局域网 OpenAI Images 兼容服务已验证：

| 项目 | 结果 |
| --- | --- |
| 模型目录 | 两个用户指定模型均存在 |
| `gpt-image-2.5-sunburst` 文字生成 | 成功；1536 × 1024，PNG，1,703,526 字节，约 13 秒 |
| 原图另存及加密历史 | 成功；资产引用与原图分别保存 |
| 重新创建存储/历史实例 | 成功；恢复图片 ID、元数据及原始字节 |
| GPT multipart 参考图编辑 | 成功；蓝杯变黄杯，1536 × 1024，1,908,176 字节；私有参考图可在重开后读取 |
| `gemini-3.1-flash-lite-image` 经该兼容服务生成 | 失败；HTTP 500，未自动重试 |

结果记录在忽略的 `work/live-image/report.json` 和 `edit-report.json`。凭据仅保存在本机加密测试配置中，未写入源码、截图或发行文件。

六家官方服务的原生协议使用模拟 HTTP 验证，没有各家的真实 Key，因此不标记为官方服务实测。真实服务测试覆盖客户端、资产、导出及重新加载；桌面完整聊天与页面操作由隔离 UI/Agent 集成测试覆盖，没有冒充逐步人工联网点击验收。

## 审查与边界

- 已修复进行中引导导致结果错绑消息、历史开关改变导致临时资产失效、显式服务拒绝误进入不明确状态、WebP 空头伪造、服务配置更新后页面引用过期、旧入口问题身份缺失、关闭最后问题窗口仍悬挂、GIF 扩展名、WPF 参数刷新与重复预设选择、Seedream Flash 不支持字段，以及移除服务商后仍运行且失去取消入口的测试请求。
- 下载限制 HTTP(S)、禁重定向、不转发凭据；只对用户明确配置的同源局域网服务允许私网图片 URL，其余来源执行公网地址检查。DNS 检查不等于底层连接固定 IP。
- WebP 执行结构及压缩分区边界校验，未引入完整解码依赖；预览仍依赖平台图像编解码器，不支持时保留原图下载和明确提示。
- Windows 剪贴板、原生另存对话框的最终人工操作，以及 macOS 原生 Keychain、剪贴板、文件对话框和完整应用重启未验收；Avalonia 检查运行于 Windows，不能替代 macOS 真机验证。
- `0.10.2-dev.2` 按用户要求同步至 dev 并创建源码标签，不创建 GitHub Release。后续后台任务/浏览器/技能安装/文档精细编辑仅保留计划。
