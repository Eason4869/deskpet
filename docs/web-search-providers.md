# 联网搜索供应商

2026-11-04 根据官方文档实现；网络请求统一经过 `ApiClient.SendAsync`，使用当前对话模型的代理配置，保留取消、超时、禁止重定向、2 MB 响应上限和安全错误消息。没有调用真实付费搜索验证账号权限；本地 fixture 测试核验请求与响应契约。

每次查询请求 5 条，返回给模型的总文本最多 16000 字符，单条最多 3000 字符，只保留合法 HTTP(S) 来源。网页结果始终放入不可信内容边界。一个会话最多执行 4 次搜索；供应商失败不会自动切换到另一家收费 API。

| ID | 服务 | 默认地址 / 方法与路径 | 鉴权 | 请求 / 解析字段 | 官方依据 |
| --- | --- | --- | --- | --- | --- |
| `serpapi` | SerpApi Google Search | `https://serpapi.com` / GET `search.json` | URL 参数 `api_key` | `engine=google`, `q`, `num=5`；`organic_results[].title/link/snippet` | [搜索 API](https://serpapi.com/search-api)、[自然结果](https://serpapi.com/organic-results) |
| `deepseek` | DeepSeek Search API | `https://api.deepseek.com` / POST `v1/tools/search` | Bearer | `query`, `count=5`, `need_content=false`；`results[].title/url/summary`，摘要回退 `snippet` | [Search](https://api-docs.deepseek.com/api/search/)、[指南](https://api-docs.deepseek.com/guides/search/) |
| `tavily` | Tavily | `https://api.tavily.com` / POST `search` | Bearer | `query`, `search_depth=basic`, `max_results=5`, 不请求答案或原文；`results[].title/url/content` | [Tavily Search](https://docs.tavily.com/documentation/api-reference/endpoint/search) |
| `baidu` | Baidu AI Search | `https://qianfan.baidubce.com` / POST `v2/ai_search/web_search` | Bearer AppBuilder API Key | 单轮 `messages`, `search_source=baidu_search_v2`, `resource_type_filter` 的 `web/top_k=5`；`references[].title/url/content` | [百度搜索](https://ai.baidu.com/ai-doc/AppBuilder/pmaxd1hvy) |
| `bocha` | Bocha | `https://api.bocha.cn` / POST `v1/web-search` | Bearer | `query`, `count=5`, `summary=true`, `freshness=noLimit`；成功 `code=200`, `data.webPages.value[].name/url/summary` | [官方技能](https://github.com/Bocha-Labs/bocha-skills/blob/main/bocha-web-search/SKILL.md)、[官方文档](https://bocha-ai.feishu.cn/wiki/RXEOw02rFiwzGSkd9mUcqoeAnNK) |
| `brave` | Brave | `https://api.search.brave.com` / GET `res/v1/web/search` | `X-Subscription-Token` | `q`, `count=5`, `extra_snippets=true`；`web.results[].title/url/description` | [Web Search](https://api-dashboard.search.brave.com/app/documentation/web-search) |
| `firecrawl` | Firecrawl | `https://api.firecrawl.dev` / POST `v2/search` | Bearer | `query`, `limit=5`, `sources=[{type:web}]`；成功 `success=true`, `data.web[].title/url/description`，不请求网页抓取 | [Search](https://docs.firecrawl.dev/api-reference/endpoint/search) |
| `exa` | Exa | `https://api.exa.ai` / POST `search` | `x-api-key` | `query`, `numResults=5`, `type=auto`, `contents.highlights=true`；`results[].title/url/highlights` | [Search](https://exa.ai/docs/reference/search) |
| `anysearch` | AnySearch | `https://api.anysearch.com` / POST `v1/search` | 可选 Bearer；空 Key 时不发鉴权头 | `query`, `max_results=5`, `format=json`；成功 `code=0`, `data.results[].title/url/snippet`，回退 `content` | [Search API](https://www.anysearch.com/docs/api-endpoints/v1-search)、[鉴权](https://www.anysearch.com/docs/auth) |
| `doubao` | 豆包搜索 Custom 版 | `https://open.feedcoopapi.com` / POST `search_api/web_search` | Bearer **联网搜索专用 API Key** | `Query`, `SearchType=web`, `Count=5`, `Filter.NeedUrl=true/NeedContent=false`；`Result.WebResults[].Title/Url/Summary` | [豆包搜索 Custom 版](https://docs.volcengine.com/docs/networked-search/networked-search?lang=zh) |

DeepSeek 查询最多 128 个 Unicode 字符，豆包最多 100 个；超长时直接失败，防止静默截断改变搜索意思。豆包的 `NeedContent=false` 不要求每个来源必须有正文，API 仍可能返回正文；优先采用 Summary，再回退 Content、Snippet。豆包搜索 Key 与 Ark 模型 Key 分别配置。

原生搜索 `native` 保留 OpenAI Responses、Anthropic、Qwen/DashScope，沿用所选模型的原生联网协议与 token 使用记录。独立搜索不虚构 token 用量。

## 自定义接口

`custom` 保留一组独立地址和密钥，中文界面显示“自定义接口”。默认地址为空；启用时必须填写完整 HTTP(S) 请求 URL，不自动追加供应商路径（公网仅允许 HTTPS，本机或内网可 HTTP）。默认接口格式 `CustomProtocol=tavily`，可选择上表十家已实现的格式；`native`、`custom` 和未知格式不会作为自定义协议，载入时回退 Tavily。

自定义接口复用所选格式的 HTTP 方法、请求参数、鉴权方式、响应解析和查询长度限制，不支持任意 JSON 模板。GET 格式只向完整 URL 追加搜索参数。允许空 Key，空时不发送鉴权头；已有 Key 时按所选格式发送。自定义配置始终保存在 `ProviderConnections["custom"]`，不会覆盖对应官方供应商的地址或 Key，也不会从官方配置自动借用密钥。

Brave 成功响应的 `web` 可为 null 或缺省：只有同时具有 `type=search` 和合法 `query.original` 才按空结果处理。豆包成功响应的 `WebResults` 可为 null 或缺省，只有 `Result.ResultCount=0` 才按空结果处理；语义错误仍拒绝。依据：[Brave 响应参考](https://api-dashboard.search.brave.com/api-reference/web/search/get)、[豆包响应参考](https://docs.volcengine.com/docs/networked-search/networked-search?lang=zh)。

## 配置与密钥

`WebSearchSettings.ProviderConnections` 为每家保存独立的 `ConnectionSettings`，`Connection` 保留旧设置兼容并指向当前供应商。旧版 Tavily 配置会迁移，Copy 深复制映射和连接，界面切换通过 ReadConnection / SetConnection 保存草稿。密钥沿用 `ConnectionSettings.EncryptedKey` 和系统加密保护，不以 `ApiKey` 明文序列化。

SerpApi 按官方契约将 Key 放入请求 URL，应用不把完整请求 URL 或 provider 原始异常写入模型错误消息。HTTP 错误沿用统一的状态码与安全说明。AnySearch 的 402 原始响应可能含自动生成的账号、密码与 Key，因此禁止把该响应原文送入日志或模型上下文。

## 未实现：Bing

微软已于 2025-08-11 退役旧 Bing Search API。Foundry Grounding with Bing Search 需要 Azure 项目、身份和模型部署，且不向开发者提供原始搜索结果，不作为本次独立搜索适配器。参见 [退役公告](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement)、[Grounding 文档](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/bing-tools)。

## 本地验证

运行 `./Build.ps1 -Target WebSearchProviderTests` 验证每家 endpoint、方法、鉴权、参数、响应字段、空结果、危险链接过滤、长度上限、取消、HTTP/语义错误、匿名请求和独立密钥加密落盘；`./Build.ps1 -Target WebSearchTests` 回归原生协议、工具循环、来源显示、权限与会话限制。所有搜索 fixture 使用本机临时端口和假密钥。
