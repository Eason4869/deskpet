# 网页搜索

在 **设置 → 工具与连接 → 网页搜索** 中配置。普通对话、快速对话和工作模式共用此设置；模型按需搜索，不会每条消息都调用搜索。默认关闭，升级后不会自动新增付费调用。

1. 打开“启用网页搜索”。
2. 选择“模型原生搜索”或已支持的独立搜索服务。
3. 原生搜索默认按当前模型的服务地址识别协议；自定义网关可手动指定 OpenAI Responses、Anthropic 或 Qwen / DashScope。网关必须实际支持对应接口，OpenAI 兼容的聊天接口不等于支持原生搜索。
4. Tavily 默认地址为 `https://api.tavily.com`，填写独立的 Tavily API Key。支持使用兼容此接口的自定义地址；仅允许 HTTPS 或现有规则允许的本地服务地址。
5. 按需选择“显示来源引用”，点击“保存搜索设置”。“还原”会丢弃本页未保存的搜索草稿。

原生搜索使用当前模型、模型密钥及代理。独立搜索服务分别保存地址和密钥，切换服务商不会混用密钥；代理沿用当前模型设置。密钥通过项目已有的 `SecretProtection` 加密后保存，不写入日志。搜索和模型调用可能分别收费。

两种方式均需要当前对话模型支持工具调用。如果服务没有提供可靠的工具能力信息，可在模型能力设置中按实际能力指定。普通对话只获得 `web_search` 工具，不会因此开启文件、终端或工作区能力。工作模式继续遵守现有联网审批规则；计划模式也可搜索。

模型会将搜索词交给后端。每次回复最多发起 4 次搜索请求，每次查询最多 1000 个字符；Tavily 每次最多返回 5 条结果，原生服务可能在一次请求内部进行多次检索。搜索可随当前回复一起取消。失败、不兼容或原生接口未返回可核实来源时，工具返回错误，不会静默改用其他付费服务。

“显示来源引用”控制应用在回复末尾补充的来源列表，不删除模型正文中自然出现的链接。原生后端的搜索摘要先作为工具结果回传给当前模型，再由当前模型组织最终回答；所以搜索发生时会有一次额外的模型请求。模型原生搜索支持情况取决于供应商、选定模型、账号权限和网关。

支持原生搜索及经过官方接口核对的独立搜索服务。各服务接口与验证范围见 [服务商说明](web-search-providers.md)。未确认或已停用的接口不放入菜单。

接口依据：[OpenAI web_search](https://developers.openai.com/api/docs/guides/tools-web-search)、[Anthropic web search](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[Qwen 联网搜索](https://help.aliyun.com/zh/model-studio/web-search)、[Tavily Search](https://docs.tavily.com/documentation/api-reference/endpoint/search)。
