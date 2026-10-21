# 图像生成与新增 Agent 工具

本功能基于 `v0.10.2-dev.1` 开发，同时提供 Windows WPF 和跨平台 Avalonia 界面。验证范围见 [验收记录](image-generation-validation.md)。

## 添加生图模型

1. 打开 **设置 → 模型配置 → 图片生成**，点击 **＋ 新增**。
2. 选择服务商，核对地址、协议和模型 ID，填写该服务的 API Key。地址和模型均可自行修改。
3. 选择默认尺寸、质量或保留“服务默认”，按需设置参考图能力、独立代理和超时。
4. 点击 **保存并使用**。需要验证时点击 **测试生成**；这会实际请求一张图片并加入图库。

配置页沿用对话模型的服务商列表、品牌选择卡片、连接设置和表单风格。图片服务及密钥与文字对话独立保存，默认超时为 300 秒。

| 预设 | 默认模型 | 图片协议与参考图 |
| --- | --- | --- |
| OpenAI Images | `gpt-image-2.5-sunburst` | Images JSON 生成、multipart 编辑 |
| Google Gemini | `gemini-3.1-flash-image` | 原生 `generateContent`、内联参考图 |
| 豆包 · Seedream | `doubao-seedream-4-5-251128` | 火山方舟 Images、JSON 参考图 |
| MiniMax | `image-01` | 原生 `image_generation`；参考图用于人物主体参考 |
| 通义千问 · Qwen Image | `qwen-image-3.0` | 百炼同步 Images、JSON 参考图 |
| xAI Grok Imagine | `grok-imagine-image-2.0` | Images JSON 生成及编辑 |
| 自定义兼容服务 | 自行填写 | 默认 OpenAI Images，可另选对应原生协议 |

预设仅填写示例，不代表账号必然具有模型权限。使用中转网关时，按网关文档选择协议；模型名称中出现 Gemini 不会自动切换为 Gemini 原生接口。百炼密钥、模型和服务区域须匹配，也可填写工作空间专属地址。

各协议只发送明确选择且支持的尺寸、质量参数。Gemini 的宽高比或分辨率在尺寸中选择，质量保持服务默认。MiniMax 的参考图不保证通用编辑效果。切换至不同服务地址会清空旧密钥，避免误传。

## 图像工作区与图库

左侧 **图像** 页面包含 **生成 / 图库** 两个标签。生成页可选择已配置模型、输入提示词、上传一张参考图、选择尺寸与质量，查看耗时或取消。缺少服务时可直接进入配置页。

图库汇总工作区、测试生成及对话工具生成的图片，以缩略图展示：

- **瀑布流**：保留图片宽高比，自动分列。
- **等大网格**：统一方形预览，只裁剪缩略图，不改变原图。
- 点击缩略图查看大图和详情，包括提示词、模型、尺寸及生成时间；支持缩放、拖动、另存原图、复制图片和打开所在目录。
- “再生成一张”先填入提示词与参数，点击生成后才发起新的请求。

可搜索提示词或模型，每次加载 40 张，并记住排列偏好。参考图独立保留以便再次生成，不作为生成结果出现在图库。

## 对话中的图片与工具

启用 Agent 工具且配置了生图服务的对话可使用 `generate_image(prompt)`。结果绑定原会话、任务和消息，直接显示图片卡片；点击可预览、复制或另存为。切换会话不改变生成结果的归属。工具首版只接收提示词，参考图编辑通过图像工作区操作。

本批同时提供：

| 工具 | 用法 |
| --- | --- |
| `ask_user` | 提问、展示 2–6 个选项或接收自由回答，提交后继续原任务；不替代操作授权 |
| `inspect_image` | 读取工作区内 PNG/JPEG/GIF/WebP，并把图像内容发送给支持视觉的模型；上限 3 MiB、2000 万像素 |
| `git_status` | 读取本地仓库状态 |
| `git_diff` | 查看未暂存或已暂存差异；未跟踪文件不含补丁 |
| `git_log` | 查看有界提交历史 |

Git 工具需要本机 Git，采用固定只读命令，不执行任意终端参数或联网。图片检查遵循现有工作目录权限；视觉内容仅进入模型请求，不把 Base64 写入工具记录或历史。关闭最后一个可回答的窗口或取消任务会取消待回答问题。

后台任务、网页搜索/浏览器操作、技能搜索安装及文档/表格精细编辑仍按 [后续模块计划](plans/2026-10-22-agent-project-tools-plan.md#后续可选模块不属于首批) 推进。

## 保存与恢复

原图保存在应用数据目录的 `generated-images` 下，索引使用现有密钥保护；聊天历史仅存资产引用和轻量信息。单图上限 50 MB、单张边长上限及总像素另有格式校验，图库索引最多 20,000 项。图片与索引均提交成功后才报告生成完成。

开启历史保存时，重启、归档和恢复会话后仍通过同一资产 ID 查看图片。关闭历史保存后新生成图片作为当前运行期间的临时数据，正常退出时清理；用户另存的副本独立保留。再次开启保存时，聊天中引用的临时资产会先转为持久资产。

文件缺失或索引损坏会显示提示，保留可恢复的原文件；可在图库执行 **恢复图库索引**。恢复扫描无法重建已经丢失的提示词等元数据。程序异常退出可能留下旧临时目录，应在确认应用未运行后自行整理。

生成请求不自动重试。取消会停止本地请求与等待，已被服务接收的任务是否终止及是否计费由服务决定。

## 协议依据

预设和请求结构依据官方接口说明核对：
[OpenAI Images](https://developers.openai.com/api/reference/resources/images/methods/generate)、
[Gemini 图像生成](https://ai.google.dev/gemini-api/docs/generate-content/image-generation)、
[火山方舟图像生成](https://docs.volcengine.com/docs/ark/image-generation-api?lang=zh)、
[MiniMax 人物参考图](https://platform.minimax.cn/docs/api-reference/image-generation-i2i)、
[Qwen 图像生成与编辑](https://www.alibabacloud.com/help/en/model-studio/qwen-image-generation-and-editing-api-reference)、
[Grok Imagine](https://docs.x.ai/developers/model-capabilities/images/generation)。
