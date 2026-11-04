# 对话搜索与设置重组验证

基线为远端 `main@18ddc82f019632e0e6703d6eae82ac09b4688bc8`，修复基线版本 1.0.1，本次开发预览为 1.0.2-dev.1。工作分支为 `codex/fix-chat-palettes-web-search`。没有修改应用最左侧的主页、图片、统计、插件和设置主导航。

| 验证 | 结果 |
| --- | --- |
| Windows `Build.ps1 -Target App` | 构建成功 |
| 菜单 `ComposerPaletteTests` | 6 项通过，包含共享草稿触发隐藏快速窗口的红绿回归 |
| 网页搜索 `WebSearchTests` | 11 项通过，覆盖普通会话、流式、工具隔离、权限、取消、次数及上下文上限、来源与密钥保存 |
| 搜索供应商 `WebSearchProviderTests` | 28 项通过，包含十家服务、自定义格式、代理、空结果与密钥加密 |
| 搜索设置 `WebSearchUiTests` | 13 项通过，含自定义格式和旧配置还原 |
| 折叠样式 `SettingsExpanderTests` | 215 项通过，含真实宽窄、亮暗、键盘和惰性内容 |
| 设置分类及完整主窗口 `SettingsOrganizationTests` | 16 项通过，确认原主导航与插件入口保留、无重复插件标签 |
| 设置隔离 `SettingsSeparationTests` | 7 组通过，覆盖异步后续草稿、运行时偏好、项目默认值及权限语义 |
| 工作设置 `WorkSettingsTests` | 70 项通过；归档删除测试改为等待实际异步删除完成 |
| 设置导航 `SettingsNavigationSmoke` | 66 项通过 |
| 设置项搜索 | WPF 中英文各 48 项通过 |
| 配置引导 `DetailedGuideTests` | 197 项通过，含精简文案、旧进度迁移和搜索/插件页定位；模型引导另有 65 项通过 |
| 显式设置保存 | 22 项通过 |
| API 回归 | 24 项通过 |
| 本地化基线 | 4530 项通过，既有资源基线检查通过 |
| WPF 英文界面审计 | 396 张截图、16031 项检查通过 |
| Avalonia Desktop / Desktop.Tests | .NET SDK 10.0.402 编译成功，无错误；存在源码警告 |
| Avalonia 设置 | 50 项通过，含自定义格式、搜索服务密钥隔离、长目录边界、亮暗主题、570/960 宽度及固定保存栏 |
| Avalonia 设置项导航 | 中英文各 31 项通过 |
| 工作区文件工具 | 87 项通过；硬链接样本需在沙箱外运行同一测试程序 |
| 差异空白检查 | `git diff --check` 通过 |

联网接口使用本地 HTTP 模拟服务验证请求与真实工具循环，没有调用用户实际账号、读取用户密钥或产生真实搜索费用。供应商模型支持、账号权限、计费及网关兼容性仍以实际服务为准。macOS/Linux 真机 GUI 和正式发布流程未执行。

主要证据位于工作树的 `work/` 日志及截图。完整 Windows 主窗口预览为 `work/settings-full-window.png`；源代码和测试说明均保存在独立工作树中，未推送远端。

以上还保留此前在本分支执行的接口、权限与界面回归记录；最终版本的本地构建信息与提交 SHA 随预览包提供。按用户要求推送 dev，不等待或核对 CI。

最终开发版补验：ReleaseVersionTests 17 项、Avalonia 引导准备 35 项通过，Avalonia 构建无错误。
