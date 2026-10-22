# 内置图像生成实施计划

> **执行约定：** 实施时按任务逐项推进；每项先补自动化验证，再实现并运行对应验证。

**目标：** 为 DeskPet 增加可配置的图片生成服务、内置图像工作区与图库，并支持 Agent 在对话中生成和内嵌展示图片，且历史会话可恢复资产引用。

**架构：** 图片生成作为独立于文字对话的客户端和资产域实现。模型配置保存多个生图服务及所选服务；工作区手动入口和 Agent 工具共用同一个生成/编辑服务与资产存储。图片字节保存在独立资产目录，聊天消息和图库索引只存资产 ID、会话/任务 ID、提示词、模型、参数和时间；聊天历史继续沿用现有加密与容量策略。

**技术栈：** Windows 使用现有 C# 5 / .NET Framework WPF；共享核心及 Avalonia 使用 .NET 10；使用现有 `SecretProtection`、`HttpClient`、`AtomicFile`、Agent 工具运行时、对话历史存储及图片预览设施。

**依据：** 用户本轮提出的生图配置与协议要求，以及补充的“图像”页面（标签生成、参考图、图库）和对话内嵌图片反馈要求；后续按用户追加要求纳入 Gemini、豆包、MiniMax、千问和 Grok 原生协议与服务商预设；不含批量生成、对话工具参考图编辑。

**并行计划：** 桌宠 Agent 常用项目工具（`ask_user`、`inspect_image`、`git_status`、`git_diff`、`git_log`）独立规划于 [桌宠 Agent 项目工具计划](2026-10-22-agent-project-tools-plan.md)，两项工作可以分别实施。

## 全局约束

**实施补充：** 已实现图库瀑布流 / 等大预览网格，点击显示详情；图片模型配置与对话模型保持风格一致。原计划中的文件名是落点建议，最终文件及测试对应关系以 [验收记录](../image-generation-validation.md) 为准。原生协议的参考图路径按各家文档实现，不统一采用 `/images/edits`；只对用户显式配置的同源局域网服务放行私网图片下载。下方未勾选项保留为完整人工联网及 macOS 真机验收，自动化与真实客户端结果已分别记录。

- 图片生成和文字对话使用分开的客户端与设置。
- 首发支持自定义模型名的 OpenAI Images 兼容服务；生成走 `/images/generations`，参考图编辑走 `/images/edits`，仅针对编辑请求上传参考图。
- 尺寸、质量可为“服务默认”；仅在用户明确选择且请求协议支持时发送参数；不统一强塞 `response_format`。
- 默认超时 300 秒；代理独立配置；用户主动取消可取消请求；生成请求不自动重试。
- API Key 通过现有密钥加密设施保存；生成图像字节不得写入聊天历史 JSON。
- 只有图片已成功落盘且资产索引已提交后，任务才进入完成态。
- 关闭聊天历史保存时，生成任务和图库只在当前应用运行期间保留；应用退出清理未另存的临时资产；用户另存文件不受影响。
- 用户手动生成支持提示词与单张参考图；Agent 的 `generate_image` 首发接收提示词，生成结果以内嵌图片卡片反馈，不以文件路径或文件名充当结果。
- 每项包含协议、存储、UI 和历史验证；自动化验证与真实服务端到端验收分别记录，不互相替代。

## 实施任务

### 任务 1：独立生图配置与服务选择

**文件：** `source/src/Settings.cs`、`source/src/TextProviderEditor.cs`、`source/src/ModelConfigurationUi.cs`、`source/src/ModelCapabilityUi.cs`、`source/desktop/DeskPet.Desktop/ProviderEditor.cs`、`source/desktop/DeskPet.Desktop/SettingsWindow.cs`、相关 `source/tests/*Provider*`。

- [x] 新增生图服务配置模型：ID、名称、Base URL、加密 API Key、自定义模型、超时（默认 300 秒）、代理开关/地址、尺寸和质量；保存多条配置及当前服务 ID。
- [x] 为旧设置文件提供默认值和向后兼容反序列化；切换服务、更新密钥及重启后配置稳定。
- [x] 在“模型配置”增加“图片生成”页签；可新增、编辑、删除、选择服务，并呈现连接状态。
- [x] 尺寸和质量控件含“服务默认”；配置变更时不泄露密钥，密钥复用现有 `SecretProtection`。
- [x] “测试生成”实际调用生成客户端，成功后显示预览并入图库；鉴权、限流、超时、取消错误能被理解。
- [x] 测试：多配置持久化/切换、旧配置升级、密钥解密、代理隔离、默认超时和测试生成错误映射。

### 任务 2：Images 客户端与图片资产仓库

**文件：** 新增 `source/src/ImageGenerationClient.cs`、`source/src/GeneratedImageStore.cs`、`source/src/GeneratedImage.cs`；修改 `source/src/Settings.cs`；新增 `source/tests/ImageGenerationClientTests.cs`、`source/tests/GeneratedImageStoreTests.cs`。

- [x] 定义 `ImageGenerationRequest`（服务 ID、模型、提示词、可选参考图、尺寸/质量）与 `GeneratedImageResult`（资产 ID、媒体类型、字节路径/流及元数据）；提供 `GenerateAsync(request, token)` 和 `EditAsync(request, token)`。
- [x] `ImageGenerationClient` 对生成发 POST `/images/generations`；有参考图时对编辑发 multipart `/images/edits`。解析 `b64_json`；对 `url` 结果执行受限 HTTP 下载，校验状态码、内容类型及图片签名后保存本地副本。
- [x] 将 API Key 仅放入请求头；按所选配置应用独立代理和 300 秒默认超时。限制重定向及 URL scheme/本机地址，防止 URL 响应下载访问本机服务。
- [x] 按服务兼容能力发送明确支持的 `size`、`quality` 参数；省略默认参数和不支持的 `response_format`。不自动重试；提供可读错误类别（鉴权、限流、超时、取消、无效响应、下载/存储失败）。
- [x] 资产仓库独立于 `ChatAttachment.MaximumBytes` 和 `HistoryStore` 的 8 MB 上限，设定单图上限 50 MB、图库索引上限 20,000 项；采用同目录临时文件、原子提交、校验后索引写入，索引损坏时保留图片并给出恢复提示。
- [x] 资产元数据保存资产 ID、来源会话/任务/消息 ID、提示词、服务 ID、模型、尺寸/质量、创建时间、原图 MIME、字节数、可选参考图资产 ID、临时/已保存状态；ID 不使用用户可控路径。
- [x] 测试：base64 与 URL 响应、错误 JSON、错误 MIME/签名、超大/损坏响应、超时、取消、403/429、URL 下载地址校验、同名任务防重、原子写失败和索引恢复。

### 任务 3：图像工作区与图库

**文件：** `source/src/ChatAppShell.cs`、新增 `source/src/ImageWorkspacePage.cs`、`source/src/ImageGalleryPage.cs`、`source/src/ImageGenerationForm.cs`、`source/src/GeneratedImageCard.cs`；必要时复用 `source/src/ChatImagePreview.cs`；新增 UI 验证。

- [x] 左侧图标列增加“图像”导航和独立路由，返回/前进记录页面路由，切回会话时不丢弃进行中的任务。
- [x] 页面包含“生成”和“图库”标签。生成页提供已配置服务选择、提示词、可选单张参考图上传/移除、尺寸/质量选择（含不指定）、开始/取消、进度与耗时。
- [x] 没有可用服务时显示直达“模型配置 → 图片生成”的操作入口；有服务但不兼容参考图编辑时说明能力并禁用上传编辑入口，不默默退化为普通生成。
- [x] 生成完成后显示可点击图片卡片；图库统一显示工作区、测试生成和对话 Agent 结果，按时间倒序，并能按提示词/模型检索、预览、下载和打开保存目录。
- [x] 下载/另存使用系统文件选择器；显示失败提示，用户取消保存不改变资产状态。
- [x] 测试：导航历史、空服务状态、生成/取消、参考图约束、图库数据刷新、筛选、浅深主题和窄窗口布局。

### 任务 4：资产引用接入聊天历史及图片预览卡片

**文件：** `source/src/PetBusiness.cs`、`source/src/HistoryStore.cs`、`source/desktop/DeskPet.Desktop/ChatMessageView.cs`、`source/src/ChatImagePreview.cs`、新增 `source/src/GeneratedImageCard.cs`、对应历史与 UI 测试。

- [x] 在 `ChatMessage` 增加可选 `GeneratedImages` 引用列表，兼容旧版本消息；每项只记录资产 ID 与轻量元数据，不存 Base64/绝对路径。
- [x] 加载历史时按资产 ID 查找本地资产；缺失/损坏资产显示占位及可恢复提示，不使整份聊天记录加载失败。
- [x] 助手消息可展示内嵌卡片，点击进入图片预览；预览支持放大/缩小/拖动，卡片和预览提供另存为及“复制图片”。复制图片直接写剪贴板图像格式。
- [x] “查看详情”显示提示词、模型、尺寸和生成时间；失败态和进行中态不创建伪资产卡片。
- [x] 新任务结果必须归属原会话和原消息 ID；切换会话期间完成时更新原会话消息/图库，不写到当前选中会话。
- [x] 测试：历史往返只保存 ID、重启恢复、旧格式迁移、资产丢失/损坏、归档/恢复、会话切换竞态、复制/另存取消、历史关闭与退出清理。

### 任务 5：Agent `generate_image` 工具和对话任务生命周期

**文件：** `source/src/AgentToolCatalog.cs`、`source/src/AgentWorkspace.cs` 或其工具分部、`source/src/ConversationSession.cs`、`source/src/ChatMessageView.cs`、聊天工具测试。

- [x] 在工具目录定义 `generate_image(prompt)`；仅当存在当前选定的可用生图服务时注册/报告可用，Agent 不得绕过用户所选服务。
- [x] 将运行时处理器接入现有 `AgentToolRuntime` 取消 token、工具事件和会话/任务上下文；给出的任务 ID、消息 ID 用于资产归属与恢复。
- [x] 运行中的助手消息显示“正在生成图片”、耗时、可点击取消；取消绑定当前生图操作，不错误取消其他并发任务。
- [x] 完成后将资产引用写入原助手消息并在消息内展示图片卡片；工具向模型返回简短成功描述及资产 ID，不把本机路径或文件名写入模型回复。
- [x] 再生成明确创建新任务/新资产，不自动重跑失败/被取消调用；Agent 只在模型具有工具调用能力且工具可用时暴露该工具。
- [x] 测试：工具 schema/可用性、成功内嵌、配置缺失、失败/限流、取消、超时、任务重复调用不自动重试、切换会话和模型不支持工具。

### 任务 6：端到端验收与回归

**文件：** `source/docs/verification.md` 或新增 `source/docs/image-generation-validation.md`；需要时更新使用说明。

- [x] 自动化覆盖两种入口、生成与参考图编辑、base64/URL 两种响应、独立资产存储、历史恢复、图库同步、取消、会话切换及原有聊天附件回归。
- [ ] 在 Windows/macOS 重点手工检查浅深主题、小窗口、图片预览缩放/拖动、剪贴板复制、另存与打开目录。
- [ ] 用一个真实兼容服务完整验收：新增配置 → 测试生成 → 工作区生成/参考图编辑 → 图库查看/下载 → Agent 对话生成/预览/另存 → 关闭并重启恢复。
- [x] 文档分别标记自动化覆盖和真实服务验收结果；未执行的真实服务步骤标记为未验收。

## Review Focus

- 服务返回外部图片 URL 指向内网/重定向地址时，客户端不得向本机或私网发请求（任务 2 URL 安全测试）。
- 生成后图片保存成功而索引/历史更新失败时，须避免消息标记完成，并允许图库扫描恢复孤儿资产（任务 2 与 4 存储故障测试）。
- 用户关闭聊天历史或删除单条会话时，临时资产清理必须尊重并发生成；另存副本不受清理影响（任务 4 生命周期测试）。
- 参考图格式、大小或服务能力不匹配时，要在请求前给出清楚错误，不将原图作为生成结果反馈（任务 2/3 编辑测试）。
- 用户快速切换伙伴/会话、窗口关闭或重启时，异步完成不得落到新会话，也不得留下无限期挂起任务（任务 4/5 竞态和取消测试）。
