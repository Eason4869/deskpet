# 0.11.2-dev.1 消息渠道计划核查

核查日期：2026-10-27。基线为 main / v0.11.1（bec057b）。按[实施计划](superpowers/plans/2026-10-27-im-channels.md)与[设计](superpowers/specs/2026-10-27-im-channels-design.md)逐项复核。代码与自动化交付项已实现；真实账号与真实设备验收仍待账号持有者执行，不能据此宣称五个平台已正式验证。

| 计划项 | 实现证据 | 结论与边界 |
| --- | --- | --- |
| 用户无公网 IP、开发者无服务器 | `src/ImChannels/*Adapter.cs`：QQ/飞书/企微 WebSocket、钉钉 Stream、微信 HTTPS 轮询 | 出站连接；无监听端口、Webhook、云中转或外部框架依赖。离线/睡眠不能服务 |
| 五平台文字协议 | 五个适配器、`FeishuFrame.cs`、`ImTransport.cs` | 夹具与网络替身通过；实际账号权限和真实收发待验收 |
| 凭据和配置保护 | `ImChannelStore.cs`、`SecretProtection` | 版本化密文、原子保存、损坏拒绝覆盖、2 MiB 上限；配置不导入模型上下文 |
| 网络边界 | `ImProtocol.Endpoint`、`ImAdapter.Request` | HTTPS/WSS、平台白名单、禁止自动重定向、1 MiB 载荷限制；不接受任意自定义地址 |
| 人格与身份隔离 | `ImMessage.Scope`、`ImConversation` | 渠道/会话/发送者独立，冻结伙伴人格与模型，不读取桌面历史，无 Agent/文件/终端等工具 |
| 允许列表与群 @ | `ImChannelProfile.Allows`、各平台 Parse | 空列表拒绝；群同时校验发送者和群；微信仅私聊 |
| 有界接收与去重 | `ImChannelManager.Admit/Work` | 回调只入队；32 排队、64 会话、40 条/64000 字符上下文、4096 ID/10 分钟；消息最长 5 分钟；不承诺跨进程去重 |
| 命令 | 宿主 `/help`、`/new`、`/stop` | 指令确定性处理；停止绕过普通队列与满额去重缓存，取消本作用域并清理对应排队 |
| 生命周期与维护 | `SaveAsync`、`StopRuntime`、维护注册、双宿主接入 | 暂停/编辑/删除等待旧任务退出；已启用连接启动恢复；鉴权失败或企微接管终止重连；维护后手动重连 |
| 微信登录 | `WeixinAdapter.LoginAsync`、`ImLoginQr`、两端设置页 | 本地 QR；取消后不保存；过期及额外验证明确提示；普通请求超时保留轮询游标。额外验证码输入流程暂不提供 |
| 心跳和就绪 | 飞书 pong 配置、钉钉 REGISTERED、企微鉴权回执 | 修复把传输建立误判就绪；修复飞书平台拒绝凭据被当作临时重连错误 |
| 回复状态 | `ImAdapter.Request`、manager 状态键 | 发送超时表示结果不确定，禁止盲目重发；不承诺平台侧绝对恰好一次 |
| 双端 UI 入口与风格 | WPF `SettingsPage` / `ImChannelSettingsPanel`；Avalonia `SettingsWindow` / `ImChannelSettingsView` | 设置导航末尾新增；沿用主题、字号、边距、表单和按钮；凭据遮罩，最近身份、允许列表、保存/连接/暂停/删除、窄窗滚动；WPF 页面复用保持草稿 |
| 浏览器入口 | `ImChannelProfile.PlatformSite`、`ImOpenPlatform` | 固定官方入口，方便创建机器人。为防命令行泄密，二维码和带令牌的扫码 URL 仅在本地 UI 展示；此项对初始设计作明确收敛 |
| 全语言 | 13 个 `assets/locales/*.json`、翻译来源指纹、`LanguagePackTests` | 52 个渠道文案全部翻译，4750 键一致，原有翻译值不变；即时切换保留草稿。品牌名沿用官方拉丁写法；未声称母语人工审校 |
| 打包与文档 | `runtime_package.py`、运行 README、`docs/im-channels.md` | 补齐安装/便携运行目录的离线指南；计划/测试不进入运行包 |
| 持续验证 | `.github/workflows/im-channels.yml` | Windows/macOS：协议、生命周期、取消、语言、打包、13 语言 Avalonia UI；Windows 增加旧编译器 WPF/QR 检查。CI 不是实机账号验收 |
| 版本交付 | `ReleaseInfo`、双语 README/CHANGELOG | 0.11.2-dev.1 向 dev 提交 PR，并在精确候选提交打源码标签；main/v0.11.1 保持原提交。不创建 GitHub Release |

## 本次修复与回归

- 消除请求验证与启动之间的取消竞态；用密钥解密时触发取消的确定性测试覆盖。
- 去重缓存满 4096 项后 `/stop` 仍能终止当前请求，且清除对应排队信号。
- 微信消息轮询和二维码状态请求的普通超时继续等待；保留消息游标，不把超时当账号失效。
- 微信 `need_verifycode` / `verify_code_blocked` 立即提示；取消后的确认结果不能保存；超长 QR 转为脱敏错误，UI 渲染错误可捕获。
- 飞书明确的凭据错误转为重新配置；应用 pong 下发的心跳间隔，拒绝零间隔忙循环。
- 钉钉只在 `REGISTERED` 之后报告就绪。
- 补齐平台名称与静态浏览器入口；将新协议、UI 和打包检查接入 CI。

## 未关闭的验收项

真实账号的登录、私聊往返、群 @、权限审核、断网/睡眠恢复和服务端限额；真实 macOS 的登录/Keychain 与恢复；微信额外验证后的账号可用性。需要账号持有者在家庭网络或手机热点中完成，不收集用户密钥。本预览保留这些限制，不把未执行的验收写成完成。
