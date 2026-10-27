# 本地消息渠道 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans task-by-task. Final independent review required.

**Goal:** 在 main 0.11.1 上交付五个平台的无服务器文字对话入口。
**Architecture:** 原生 C# 适配器、共享权限与生命周期、加密存储、独立有界会话、WPF/Avalonia 双宿主。
**Tech Stack:** .NET Framework / .NET 10，HttpClient、ClientWebSocket、DataContractJsonSerializer、SecretProtection。
**Spec:** ../specs/2026-10-27-im-channels-design.md

## Global Constraints

用户无公网 IP；开发者无服务器。无需额外安装 Node/Python/机器人框架。远程对话不开放工具。空允许列表拒绝。敏感信息不进日志。保留 0.11.1 版本号直到明确发布请求。

## Review Focus

1. 平台返回的动态 URL 跨域泄露密钥：网络白名单与重定向测试。
2. 暂停/退出后旧实例继续发送：生命周期代次和取消测试。
3. 群聊与桌面私聊串上下文：身份范围与上下文测试。
4. 配置损坏时保存覆盖：失败封闭与原子存储测试。
5. 持续推送撑爆队列/线程：有界接收、去重和过期测试。

### Task 1: 共享配置、存储和协议契约

Create: src/ImChannels/ImChannelProfile.cs、ImChannelStore.cs、ImProtocol.cs；tests/ImChannelTests.cs。
- [x] 写允许列表、作用域、密文与损坏文件测试并观察失败。
- [x] 实现版本化存储、消息契约和网络校验；加入共享核心编译。
- [x] 运行针对性测试并记录结果。

### Task 2: 平台出站连接

Create: src/ImChannels/ImTransport.cs、QqAdapter.cs、WeixinAdapter.cs、FeishuAdapter.cs、FeishuFrame.cs、DingTalkAdapter.cs、WecomAdapter.cs。
- [x] 写各平台真实载荷夹具、回复目标和非文字处理测试并观察失败。
- [x] 依据官方协议实现网络、鉴权、心跳、收发及取消。
- [x] 验证动态域名限制、长度边界、protobuf 和消息关联。

### Task 3: 会话路由与生命周期

Create: src/ImChannels/ImChannelManager.cs、ImConversation.cs。
Modify: src/PetWindow.cs、desktop/DeskPet.Desktop/DesktopController.cs、维护集成。
- [x] 写未知发送者拒绝、去重、隔离、队列上限、停止后不回复与独占测试。
- [x] 实现有界路由、无工具模型调用、维护闸门、宿主退出和启动恢复。
- [x] 运行针对性测试与既有对话回归。

### Task 4: 双端设置

Create: src/ImChannelSettingsPanel.cs、desktop/DeskPet.Desktop/ImChannelSettingsView.cs。
Modify: 两端设置导航与 13 个 assets/locales/*.json。
- [x] 添加两端导航/UI 断言，确认当前缺失。
- [x] 实现主题一致的渠道列表、允许列表、保存、连接、暂停、删除和微信二维码绑定。
- [x] 验证即时语言刷新、保留草稿、窄窗、密码隐藏和错误显示。

### Task 5: 验证与交付

- [x] 更新 README.md / README.en.md / CHANGELOG.md / CHANGELOG.en.md / docs/im-channels.md。
- [x] Windows 构建、.NET 10 核心和 Avalonia 构建，针对性与必要回归测试。
- [x] 独立代码审查并修复重要问题；记录真实账号验收尚未执行的部分。

## Execution Ledger

- Baseline: git fetch origin main --tags 成功；main 与 v0.11.1 均为 bec057b；独立分支 codex/im-channels-0.11.1。
- Ruling: 用户已明确要求按现有方向开发，补齐设计与计划后继续实现，不再次请求泛化方案批准。

- Task 1: complete — 加密配置与损坏/超限保护，核心测试观察缺失类型失败后通过。
- Task 2: complete — 五平台原生出站传输、固定域名边界、飞书 protobuf 黄金帧与分片、QR 本地编码；真实账号验收单列待执行。
- Task 3: complete — 使用 ConversationSession，强制关闭 Agent/语音/持久化并冻结人格和模型；隔离、去重、停止、撤销、容量和终止事件测试通过。
- Task 4: complete — 两端设置入口、密码遮罩、即时语言与草稿、浅深主题、窄窗、保存和删除覆盖通过。
- Task 5: complete for code delivery — Windows/.NET10 构建、35 核心项、生命周期场景、7 WPF UI / 6 Avalonia UI、66 原导航、356 语言包、13x128 回复语言回归通过。真实账号与真实 Mac 验收不在已完成项内。
- Final: fixed WeCom connection takeover — 官方 disconnected_event 分类与 manager 终止/不重连回归通过；并拒绝重复机器人。
- Final: fixed oversized configuration — 保存拒绝且旧文件字节不变、仍可读取；回归通过。
- Final: fixed context bound — 超大单轮不能突破 IM 上限，推理不留入上下文；回归通过。
- Final: fixed stale deletion list — Avalonia 保存/删除后列表为空回归通过，两端均重新加载列表。
- Final review: independent reviewer completed; all four actionable findings fixed. Deferred review minors: none.
- Delivery limits: no real-account tests or real Mac acceptance; non-Chinese/English explanatory copy partially remains English. See docs/im-channels-acceptance.md.
