# 中文文案资源维护

`0.10.5-dev.1` 将 WPF、Avalonia、macOS 平台层和共享业务提示集中到唯一的 `assets/locales/zh_CN.json`。目前仅支持中文，不提供语言选择、外置语言包或运行时语言切换。

使用稳定英文功能路径作为 Key。固定文字调用 `TextResources.Get("common.cancel")`；带参数的整句调用 `TextResources.Format("feature.result", value)`，参数使用 `{0}`、`{1}`。需要原有区域规则时调用带 `IFormatProvider` 的重载，不改变线程 Culture。普通大括号文字用 Get；格式模板内的字面大括号写成 `{{` 和 `}}`。JSON 必须为 UTF-8 无 BOM 的扁平字符串对象，不允许重复 Key、空白 Key 或非字符串值。

新增文字和调用点同步提交，并在 `migration-inventory.jsonl` 中记录来源提交、文件、上下文、文字哈希、分类及资源 Key。在 `tests/fixtures/localization/text-baseline.json` 保留独立预期，避免测试仅与实现使用同一个资源值。`Format` 的占位符、参数位置、空值、路径、引号、换行及原有数字/日期区域规则必须验证。

模型系统提示词、工具 schema、人格正文、品牌/角色身份、旧配置兼容值及模型/用户内容作为数据保留。排除必须有逐项理由、审核标记与具体源码行哈希；改变其上下文会触发门禁。界面显示和业务行为分别使用文案及明确状态，禁止比较显示文字决定动作。已经移除的旧比较点用 `retired_reason` 解释；闲置资源必须在 `retired-keys.json` 解释，不能为通过检查批量删除。

读取器从定义 `TextResources` 的程序集加载 `Dafeiyu.Locales.zh_CN.json`，以线程安全 Lazy 缓存一次。未知 Key 返回 `[key]`，每个目录的每个未知 Key 只记录一次诊断。缺失或损坏整个资源时抛出 `InvalidDataException`；启动错误通道保留独立兜底“界面文字资源不可用，请修复安装。”，不递归读取损坏的资源。

开发检查：

```powershell
python tests/LocalizationAuditTests.py
python tools/verify_localization.py inventory --baseline 4944d11416fec7b494a8eb8ac3c4b8376e1100fd
python tools/verify_localization.py check --phase complete --report work/localization-audit.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationMissingResourceTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationUiTests
```

共享默认测试包含 LocalizationTests；Avalonia 默认和 `--localization-ui-only` 包含专用 UI 检查。Windows/macOS CI 均运行完整性门禁；Windows 验证 WPF 主程序、更新器及实际安装成品，macOS 验证应用包内实际 Core 的资源字节及读取。产物检查只接收预期 SHA256 和程序集路径，不依赖外部 JSON。

原始资料见 [语言计划](planning/2026-10-23-zh-CN-json-refactor-plan.md) 与 [统一计划](planning/2026-10-23-localization-and-agent-background-jobs-plan.md)。资料中的后台任务是后续范围；本次版本仅实施 S0–S7。原资料的“不改版本”由用户明确指定 `0.10.5-dev.1` 覆盖。
