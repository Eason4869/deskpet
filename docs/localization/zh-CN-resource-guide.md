# 中文文案资源维护

`0.10.5-dev.1` 已将 WPF、Avalonia、macOS 平台层和共享业务提示集中到中文规范目录 `assets/locales/zh_CN.json`，该版本只做中文资源重构。`0.10.5-dev.2` 增量提供 13 个内嵌语言目录和语言选择弹窗，详见 [多语言设计](multilingual-design.md)。`0.11.0-dev.2` 在此基础上增加语言热切换；开发验收见 [热切换验收](live-language-acceptance.md)。不提供外置语言包加载。

新设置、没有语言字段的旧设置和未知代码默认保存 `system`，启动时按系统界面语言解析一次；手动保存的语言保持不变。保存语言后原位刷新现有界面，无需重启；引导页使用“应用并继续”。现有任务和连接保留启动时的语言快照，新请求使用新语言；历史及用户自定义内容保持原文。语言名称使用本名，“跟随系统”使用当前界面语言；葡语匹配巴西葡语，西语区分西班牙和拉美，未适配的系统语言使用 English。

使用稳定英文功能路径作为 Key。固定文字调用 `TextResources.Get("common.cancel")`；带参数的整句调用 `TextResources.Format("feature.result", value)`，参数使用 `{0}`、`{1}`。需要原有区域规则时调用带 `IFormatProvider` 的重载，不改变线程 Culture。普通大括号文字用 Get；格式模板内的字面大括号写成 `{{` 和 `}}`。JSON 必须为 UTF-8 无 BOM 的扁平字符串对象，不允许重复 Key、空白 Key 或非字符串值。

新增文字和调用点同步提交，并在 `migration-inventory.jsonl` 中记录来源提交、文件、上下文、文字哈希、分类及资源 Key。在 `tests/fixtures/localization/text-baseline.json` 保留独立预期，避免测试仅与实现使用同一个资源值。同步补齐其余 12 个语言包，更新成品测试 SHA256 和翻译来源记录。原四包保留已核验的 CRLF 字节约定，其他包使用 LF；都不含 BOM。`Format` 的占位符、参数位置、空值、路径、引号、换行及原有数字/日期区域规则必须验证。

工具协议字段、品牌/角色身份、旧配置兼容值及已保存的模型/用户内容作为数据保留。按用户对 dev.2 的追加授权，官方显示文案和默认人格进入 JSON；只精确识别默认模板，自定义人格不改写，请求附加实际生效的回复语言要求。排除必须有逐项理由、审核标记与具体源码行哈希；改变其上下文会触发门禁。界面显示和业务行为分别使用文案及明确状态，禁止比较显示文字决定动作。已经移除的旧比较点用 `retired_reason` 解释；闲置资源必须在 `retired-keys.json` 解释，不能为通过检查批量删除。

读取器从定义 `TextResources` 的程序集加载 `Dafeiyu.Locales.<locale>.json`，以线程安全 Lazy 缓存。中文规范目录提供运行时安全回退，但验收要求所有语言的键集合完整一致，不能依赖中文回退冒充翻译。未知 Key 返回 `[key]`，每个目录的每个未知 Key 只记录一次诊断。缺失或损坏整个资源时抛出 `InvalidDataException`；启动错误通道保留独立兜底“界面文字资源不可用，请修复安装。”，不递归读取损坏的资源。

开发检查：

```powershell
python tests/LocalizationAuditTests.py
python tests/HelperResourceTests.py
python tools/verify_localization.py inventory --baseline 4944d11416fec7b494a8eb8ac3c4b8376e1100fd
python tools/verify_localization.py check --phase complete --report work/localization-audit.json
python tools/verify_language_packs.py --report work/language-pack-audit.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationMissingResourceTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationUiTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target SystemLanguageTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LanguagePackTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target ReplyLanguagePolicyTests
```

共享默认测试包含 LocalizationTests；Avalonia 默认和 `--localization-ui-only` 包含专用 UI 检查。Windows/macOS CI 均运行完整性门禁；Windows 验证 WPF 主程序、更新器及实际安装成品，macOS 验证应用包内实际 Core 的资源字节及读取。产物检查只接收预期 SHA256 和程序集路径，不依赖外部 JSON。

语言启动、人格请求、选择弹窗和完整界面矩阵每种语言使用独立进程；完整矩阵生成应用控件 PNG、文字清单和排除项，不是桌面截图。结构与格式检查不代表母语人工审校。独立更新器只能包含逐包字节/哈希一致的内嵌 JSON；这些资源扣除后，代码、PE 元数据和其他未解释字节仍须小于 1 MiB，不用总文件大小猜测是否混入角色素材。

原始资料见 [语言计划](planning/2026-10-23-zh-CN-json-refactor-plan.md) 与 [统一计划](planning/2026-10-23-localization-and-agent-background-jobs-plan.md)。dev.1 的 S0–S7 已完成；dev.2 按用户后续授权执行 S12a 多语言和 Agent 后续任务。历史计划的范围限制不覆盖用户追加要求；完整版本验收仍须按统一计划收口。

## 0.11 新功能文案的独立来源

历史迁移目录和 `text-baseline.json` 继续保护原有文字及审核来源。新功能在旧 C# 中没有历史中文原句，因此不补造 `decision=migrate`、历史提交或母语人工审阅。

- 固定原始保护提交为 `fde04835ffce544c334205d881a232c579ded4df`，不是可由证据 JSON 互相改写的“最新基线”。`protected-originals.json` 逐语言保存原 4507 个值的规范哈希，并与该提交、原始键集合、原独立文本基线及迁移记录逐项核对。历史记录只允许给确已替换的旧排除上下文补充 `retired_reason`；原值、哈希、来源、分类和原审核标记保持不变。
- `tests/fixtures/localization/feature-text-baseline.json` 是独立维护的新功能中文预期和 Get/Format 占位符合同。必须精确覆盖所有非历史键，重复、缺失、额外字段/记录和占位符漂移均失败。语言检查同时验证新旧 Format 合同；运行时测试嵌入独立新合同，不能仅用当前资源与自己比较。
- `feature-resource-provenance.json` 将每个新键绑定到真实先前功能快照的 `originCommit/originKey/originValueSha256`、最终中文哈希、13 种语言的值哈希，以及具体生产文件/源码行上下文/出现次数。`resourceCommit` 必须是实际包含最终资源与调用点的提交，存在且为当前 HEAD 祖先；逐项读取提交内容校验。`originCommit` 表示来源快照，不冒称第一次引入或历史迁移。只有六项明确的 first_meeting→firstMeeting、best_friend→bestFriend 资源映射可更换源键；保存的阶段 ID 不改名。
- 先提交产品与版本，再以该实际提交绑定来源元数据，避免自引用 SHA。源码/译文后续有变化，需要新的源码提交与重新审阅的精确来源记录。CI 必须检出完整历史；缺失对象不视作通过。
- `sourceExclusions` 只接受实际存在于工作树和所绑定提交的具体文字、文件、行上下文及逐项理由，用于稳定文件身份、编译期元数据或原有模型指令等非 UI 内容。没有按前缀豁免、任意 .Get 扫描或批量排除。真正闲置资源继续逐键写入 `retired-keys.json`，不删除原值。
- 正常调用仍使用字面 `TextResources.Get/Format`。需要 S1 明确语言时，使用 `Get("literal.key", catalog)` 与窄静态 `FormatForCatalog(catalog, "literal.key", args)`，同样检查字面量、格式与参数，不改变旧 public provider overload，也不切换进程语言。任意对象的 `.Get` 不会被宽泛当作已审资源引用。

来源声明 `assistant-authored / nativeHumanReview: not_performed` 表示助手生成且没有母语人工审校。`technicalReview: pending_parent_review` 记录元数据生成时的状态；完整技术审阅由本任务的独立父级审查执行并单独记录，不能把结构检查或自查伪称人工语言审核。旧语言包 provenance 的 dev.2/dev.3 哈希仍是历史阶段记录，新版本以独立 feature provenance 和实际成品哈希为准。

新增维护检查：`python tests/FeatureResourceProvenanceTests.py`；离线标题字库检查：`python tools/fonts/relationship_title_fonts.py --check` 及 `python tools/fonts/test_relationship_title_fonts.py`（依赖只用于资产准备/验证）。翻译阶段标题后运行 `--build` 重建全部子集、语料、manifest 和生成的覆盖清单，保留 Devanagari RA/VIRAMA 连写支持及上游/OFL 固定来源。

现有控件请使用 `LocalizedUi` 或接受 `Func<string>` 的显示辅助方法绑定原始资源键及格式参数；不得对译文反查键、重建整个窗口或刷新输入草稿。活动系统通知使用 `TextResources.Message` 保存键和参数。后台模型请求通过 `LanguageRequestScope` 保留一次任务的语言，界面 `TextResources.Get` 始终读取当前有效语言。
