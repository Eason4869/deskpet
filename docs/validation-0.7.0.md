# 0.7.0 合入检查

本次将 Windows 会话归属、项目交互、富文本 Markdown 编辑、非中断引导和细分配置教程整合进 dev。它改变了主要使用流程与持久化归属，因此从 0.6.3 升为 0.7.0，Windows 与共享桌面项目的产品／程序集版本保持一致。正式交付采用 v0.7.0 标签与 Release：Windows 安装／便携版，以及明确标注 preview 的 macOS 两架构 DMG／ZIP。

## 当前验证

- Windows CI 矩阵共 63 组测试，完整运行并对修正涉及的项目复验后全部通过。矩阵包含编辑器、资源生命周期、会话归属、存储恢复、项目设置、双模型协议、队列、引导、文档、外观和更新。
- 共享核心回归 330 项通过；Avalonia 无头界面／资源／动画断言 1604 项通过。
- 最终输入区与气泡排版 76 项通过，覆盖直接编辑、纯文本切换、撤销重做、嵌套列表缩进，以及 GitHub Logo／普通网站地球图标与真实链接目标。
- 完整对话集成 85 项、Composer 综合 87 项、Agent 工作台 38 项、早期修复回归 14 项通过。旧的双层 Markdown 预览和“默认项目”控件断言已按实际界面更新；模型夹具绑定目标会话，不再错误等待新建工作区的真实客户端。
- `ReleaseAssetTests.py` 与 `PackagingSmoke.py` 通过：从干净公开源码构建，完整 ZIP 可由真实更新引擎应用并保留数据；离线配置引导及 README 配图存在；源码与运行包排除凭据、用户数据及本地工作文件。
- 修改内容的常见凭据模式扫描无匹配，`git diff --check` 通过。这不替代独立安全审计。

## 审查中修正

1. 直接编辑列表后，保留嵌套列表与同一列表项的多段落缩进。
2. 普通聊天隐藏工作专用目标与计划菜单；快速对话不提供模式切换命令。
3. Avalonia 首次启动保持聊天模式；Mac 不继承 Windows 默认工作模式。
4. 打包与安装清单包含新的 `configuration-guide.md`；Build.ps1 的编码兼容 Windows PowerShell 5 的中文文件名。
5. 链接统一为图标加可点击文本。GitHub 使用专用 Logo，其他网站使用地球图标；输入区用 Ctrl + 单击打开，普通点击继续编辑。不因渲染链接自动访问网站或请求图标。

## 复现入口

Windows 测试清单位于 `.github/workflows/check.yml`，通过 `Build.ps1 -Target <名称>` 串行运行。共享核心使用 `dotnet run --project desktop/DeskPet.Tests -- --report-json <路径>`；跨平台界面使用 `dotnet run --project desktop/DeskPet.Desktop.Tests`。打包回归使用 `python tests/ReleaseAssetTests.py` 与 `python tests/PackagingSmoke.py`。

详细本地原始日志保存在被 Git 忽略的 `work/release-070-*.log`；它们不是发布附件。

## 验收边界

本地模型、语音与网络多数使用确定性的模拟服务；不宣称真实云端服务、麦克风、听感或 macOS 原生设备已验收。Mac 不包含本轮 Windows 的新版侧栏、直接编辑 Markdown、118 步配置教程或文件 Agent。远端 Actions 结果以对应提交的实际运行记录为准。
