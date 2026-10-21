# 局域网中转 API 验证

日期：2026-10-22。在 0.10.1-dev.2 基线及此前技能/对话草稿实现上增量修改，分支 codex/skill-repository-discovery。

## 修改

- ApiClient 的 HTTP 地址与可空密钥校验统一识别回环、RFC1918 IPv4、169.254 链路本地、100.64/10 共享 IP，以及 IPv6 ULA/链路本地/映射 IPv4。
- 使用 IP 字面量判定，不在界面保存时发起 DNS 查询。公网 HTTP、URL 内账号、查询参数、片段及密钥换行仍拒绝；TLS 校验、重定向策略和密钥落盘加密保持原有行为。
- 配置页面提示明确支持 HTTPS 与本机/局域网 IP 的 HTTP。Gemini Live 的 WebSocket 配置保持其独立校验。

## 测试

| 检查 | 结果 |
| --- | --- |
| LanServiceTests 地址矩阵 | 41 项通过；含 IPv4 范围边界、IPv6、映射地址、Tailscale 网段、无密钥、本机兼容、完整路径及不合规地址 |
| LanServiceTests --live-lan | 在本机实际 192.168.1.9 网卡上临时绑定测试中转，额外 8 项通过：模型列表、OpenAI/Anthropic 聊天及鉴权、SSE 流式、ASR、TTS。合计 49 项 |
| LanSettingsUiTests | 4 项通过：可见配置页保存 HTTP 内网地址且允许空 Key、密钥加密、重开恢复、拒绝公网 HTTP 并保留已保存配置；已检查浅深截图 |
| ApiTests | 24 项通过：模型、聊天、音频、代理、重定向、超时与取消等回归 |
| StreamingTests | 24 项通过：流式分块、协议、取消、超时、鉴权与 UTF-8 回归 |
| DeskPet.Core .NET 10 | 0 错误，存在既有平台/API 警告 |

本轮共 101 项检查通过（49 + 4 + 24 + 24）。TCP 接口测试在沙箱外执行，不接触实际用户中转账号。测试内容和音频均为固定 fixture；未访问用户实际自搭服务器，也不代表外部中转兼容性人工验收。此前技能、内置弹层和草稿改动保留。

旧 ChatSmoke 在定位独立 SettingsWindow 的过时断言处停止；当前应用已将配置嵌入主窗口，新增 LanSettingsUiTests 直接验证正在使用的 ConnectionSettingsPanel。没有将该旧测试算作通过，也未为本次需求重写旧窗口测试。远端 CI 未在本轮运行。

日志：work/lan-service-live-final.log、work/lan-settings-ui.log、work/api-lan-regression-final.log、work/lan-streaming-regression.log。配置截图见 work/lan-settings-*。
