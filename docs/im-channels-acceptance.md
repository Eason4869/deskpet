# 本地消息渠道交付验证

日期：2026-10-27。基线：main / v0.11.1，bec057b0dd90316be4f3be635b0e5f00ef1e8748。
开发分支：codex/im-channels-0.11.2-dev.1。显示版本 0.11.2-dev.1，数字版本 0.11.2.0；main / v0.11.1 保持原提交。仅提交 dev PR 和源码标签，不创建 GitHub Release。

## 已执行

| 检查 | 结果 |
| --- | --- |
| ImChannelTests | 35 项通过；覆盖五平台载荷、加密存储、超大保存拒绝、原文件保留、网络目标限制、protobuf 黄金帧及分片、QR |
| ImChannelLifecycleTests | 原有隔离、人格、无工具、上下文上限、允许列表、去重、过期、排队、停止、撤销、删除、互斥和接管场景通过；新增满 4096 去重项仍能停止、取消保存不落盘 |
| ImChannelAuditTests | 验证阶段取消竞态、微信超时保留游标、额外验证码、飞书鉴权错误、pong 心跳策略、钉钉订阅就绪、12 个非英语语言的长文案及 QR 超限通过 |
| WPF ImChannelUiTests | 9 项通过；包含单文件 QR 依赖加载、五平台、密码遮罩、即时语言切换保留草稿、页面往返、固定浏览器入口、浅深主题与窄窗 |
| Avalonia --im-channels-ui-all-locales | 13 种语言各 7 项通过，共 91 项；导航、遮罩、草稿、五平台、保存、删除、浏览器入口；逐语言截图与窄窗检查 |
| 既有 SettingsNavigationSmoke | 66 项通过，原导航索引保持稳定 |
| LanguagePackTests | 408 项通过（含新增 52 个渠道文案契约） |
| 回复语言回归 | 13 个语言，每个 128 项通过 |
| SpeechLanguagePolicyTests | 34 项通过 |
| verify_language_packs.py | 13 个语言均 4750 个键，无缺失/多余键，格式参数与独立字节快照通过 |
| 原有翻译保护 | 相对基线仅增加 52 个 im.* 键，全部渠道说明/诊断均翻译；既有翻译值逐项保持一致，受保护四个语言包保持原 CRLF 约定 |
| Python 打包/发行契约 | RuntimePackageTests 5 项、ReleaseAssetTests 4 项、WindowsReleaseTests 1 项通过；包含离线教程及本地链接 |
| 版本/构建身份 | ReleaseVersionTests 15 项、ReleaseIdentityTests 通过，显示预览后缀与数字程序集版本分开 |
| 翻译证据校验 | FeatureResourceProvenanceTests 10 项、LanguagePackSnapshotTests 1 项通过；受保护历史证据不改写 |
| Windows App / .NET 10 Core / Avalonia | 均完成编译；既有平台 API 与资源反序列化警告仍存在 |
| 独立代码审查 | 已执行；四个发现均已修复，见下文 |

语言补充的独立哈希与未修改既有翻译的证据记录于 `localization/im-channel-supplement-provenance.json`。13 个语言的渠道文案均已翻译，品牌名保留官方写法；未声称母语人工审校。完整计划逐项结论见 [计划核查](im-channels-plan-audit.md)。

## 审查修复

1. 企业微信 `disconnected_event` 作为终止状态，取消待处理任务且不自动争抢连接；同一机器人不能在两个活动配置中连接。
2. 加密配置序列化后先检查 2 MiB 限制，再替换文件；超限拒绝并保留可读取的旧文件。
3. 仅保留完整 user/assistant 轮次，剥离推理文字；超大单轮直接从记忆中移除，完成后严格限制 40 条 / 64000 字符。
4. 删除后重新加载两端列表，旧配置与凭据不能通过残留条目重新选回。
5. 本次新增取消竞态、满额停止、微信轮询游标/额外验证/扫码取消、飞书鉴权/心跳、钉钉就绪等修复与回归；详见计划核查。
6. 新增跨 Windows/macOS CI；所有状态/平台文案通过显式资源映射，保留现有本地化审计边界。

## 复现命令

```powershell
dotnet build desktop/DeskPet.Tests/DeskPet.Tests.csproj --no-restore -m:1 -p:NuGetAudit=false
dotnet desktop/DeskPet.Tests/bin/Debug/net10.0/DeskPet.Tests.dll --im-channels-only
dotnet desktop/DeskPet.Tests/bin/Debug/net10.0/DeskPet.Tests.dll --language-packs-only
dotnet desktop/DeskPet.Tests/bin/Debug/net10.0/DeskPet.Tests.dll --reply-language-only
python tools/verify_language_packs.py
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target ImChannelUiTests
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target SettingsNavigationSmoke
dotnet build desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj --no-restore -m:1 -p:DeskPetFullResourceFixtures=false
dotnet desktop/DeskPet.Desktop.Tests/bin/Debug/net10.0/DeskPet.Desktop.Tests.dll --im-channels-ui-all-locales
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target App -OutputDirectory work/windows-app
```

新工作树首次运行前需恢复既有 .NET 项目依赖。`-m:1` 用于当前 Windows 环境的 MSBuild 依赖图问题，不改变应用运行行为。

## 尚未执行

- 未登录真实 QQ、微信、飞书、钉钉或企业微信账号，未使用真实机器人发送/接收消息。
- 未验证账号实际开放范围、配额、发布审核、公众号能力、微信额外验证码流程。
- 未在真实 macOS 上执行登录、Keychain 与网络恢复验收。
- 本记录描述本地验证；PR/源码标签及 CI 状态以 GitHub 对应提交为准。未发布二进制 Release，未将功能合入 main。

发布前由账号持有者在家庭网络/手机热点完成：私聊往返 → 支持的群 @ → 陌生人被拒绝 → 断网/睡眠恢复 → 暂停后不回复 → 重新登录 → 账号/人格隔离。不可将自动测试通过写成真实账号验收通过。
