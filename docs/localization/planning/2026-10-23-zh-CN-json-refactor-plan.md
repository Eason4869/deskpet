# 应用中文文案集中到 zh_CN.json 的实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. 使用下方复选框记录实施状态。本文件仅为规划，所有实施步骤尚未执行；本轮不修改应用代码和测试。

**Goal:** 将 DeskPet 应用自身的中文界面和用户可见提示集中到 `zh_CN.json`，保持原有中文显示、功能和数据兼容性，为以后翻译提供统一入口。

**Architecture:** 两套界面及共享业务层共用一个只读文字资源入口，JSON 随程序作为资源嵌入。按功能逐批替换字符串，先处理依赖中文显示文字的判断，再迁移界面与提示；通过文案基线、源码审计、功能回归和打包检查防止遗漏。

**Tech Stack:** C#、Windows WPF/.NET Framework 4.8、共享 .NET 10 核心、Avalonia、现有 JSON 序列化兼容层、PowerShell/Python 检查脚本；不新增运行时第三方依赖，不使用 Docker。

**Spec:** 本文第 2～5 节为本计划的完整需求及资源合同；本次不依赖已删除的历史规划文档。

## Global Constraints

- 只重构为 JSON，暂不翻译。
- 资源文件名固定为 `zh_CN.json`，内容为简体中文，UTF-8 无 BOM。
- 不改文字措辞、标点、换行、快捷键提示、可见空格和占位参数含义。
- 不改变布局、字体、颜色、响应式行为、浅色/深色主题和原有功能。
- 不增加语言选择、系统语言检测、即时切换、外部语言包加载和在线下载。
- 不修改聊天历史、用户输入、项目文件、模型返回内容和用户自定义名称。
- 不借机改变审批、工具、终端、图像任务、网络、收费请求和应用退出逻辑。
- 不升级现有 JSON 依赖，不改变旧配置/历史数据中的稳定值。
- 保留实施时现有未提交改动；本计划不授权合并、推送、发版或修改版本号。

## Review Focus

1. 中文被用于状态/菜单/默认值判断：迁移后交互、着色、参数和分组必须保持一致，归任务 3。
2. 动态文案中的大括号、换行、空值和数字格式：显示与原表达式一致，归任务 2、6。
3. 旧配置和历史中的中文名称：不能因抽取资源而丢失识别或被改写，归任务 3、6。
4. 构建/精简包漏嵌入 JSON：离开源码目录的应用仍能正常显示，归任务 2、7。
5. 扫描将提示词/内部标记误迁或漏迁用户提示：逐项分类，不能全局机械替换，归任务 1、7。

---

## 1. 核对基线与工作量

分析时统计基线为 `7f2e0c38fddd802ac00f6807f4896d1034753424`；本文进一步核对的本地审查副本 HEAD 为 `03170c127d90abdab29ddae2bdf5912027d4055c`。后者是本地已提交代码，不在本文中声称已重新确认远端相同。

| 范围 | 含中文字符串的文件 | 含中文字符串出现次数 |
| --- | ---: | ---: |
| src：WPF 与共享业务代码 | 223 | 4,076 |
| desktop：Avalonia 生产代码 | 37 | 790 |
| 合计 | 260 | 4,866 |

统计来自 C# 字符串词法近似匹配，不是精确可迁移条目数，不等于用户界面词条数；重复文字、模型指令、内部标记等均可能包含其中。测试中的中文另外统计，不作为待翻译应用文案。本轮未对新 HEAD 重新完整统计；任务 1 必须生成实施基线。

预计单人有效工时 **22～37 小时，约 3～5 个工作日**。耗时主要是逐处判断和回归，不是 JSON 读取。远端 CI 排队、机器环境修复和后续新需求不包含在此承诺中。

## 2. 范围与完成定义

### 2.1 必须迁入 JSON

- 两套界面的标题、按钮、菜单、标签、占位提示、Tooltip、空状态、确认框、可访问性文字。
- 应用自身产生并向用户展示的任务状态、进度、成功/失败/取消提示、参数说明。
- 设置、配置引导、使用引导、快捷键说明、关于/更新、角色/服饰管理的界面说明。
- 业务层中实际展示给用户的校验错误、服务/下载/保存错误说明。
- 动态句子的固定中文部分，作为完整模板存储，不拆成零散汉字再拼接。
- 原有纯英文但属于可翻译的固定界面词句也纳入；协议/品牌/技术标识原样保留。前面的中文粗统计不覆盖这些调用点，需在任务 1 补充清点。

### 2.2 本轮保留原位，逐项注明原因

- 模型系统提示词、工具说明/schema 中发给模型的指令文本、人物人格正文与自动命名提示词：避免改变 Agent 行为。
- 用户保存的名称、既有聊天记录、服务响应、第三方异常原文、命令输出和项目文件内容：这些不是应用翻译资源。
- 用于旧数据兼容、协议、迁移识别和持久化的中文常量：保留原始稳定值，必要时另做显示映射。
- 日志中的开发诊断、测试输入/期望、测试报告标题：不要求迁入应用语言包。
- 原本已经数据化的角色包人格、节日问候、素材文字、文档和图片内嵌文字：不合并到本次界面资源文件。
- 数字、路径、协议名、模型名、品牌名等动态数据：保留数据本身，仅抽取其外层说明。

同一段文字同时用于模型和 UI 时，只迁移展示路径，模型路径保持原输入；不得仅因包含中文就搬走。设置默认显示名需区分“新建时的标签”与“旧配置里的身份值”。

### 2.3 完成必须同时满足

1. 全部纳入范围的调用点从 JSON 获取中文，剩余中文逐项有排除理由。
2. 不止创建 JSON：旧字符串实际调用路径已完成替换。
3. 原有中文显示和行为保持一致，旧配置无需迁移。
4. WPF、Avalonia、共享核心及其测试使用同一份资源源文件。
5. 开发构建、便携/精简包、安装后应用均能读取内嵌资源。
6. 检查脚本和测试登记进现有构建/CI，新增硬编码文案能够被发现。

## 3. 资源文件与命名规则

源文件固定为 `assets/locales/zh_CN.json`，顶层为扁平 JSON 对象，值全部是字符串。例：

```json
{
  "common.copy": "复制",
  "common.cancel": "取消",
  "image.tasks.loadMore": "加载更多任务",
  "image.tasks.currentModel": "当前模型：{0}",
  "image.tasks.elapsed": "耗时 {0} 秒"
}
```

规则：

- Key 使用稳定的英文功能路径：`common.*`、`settings.*`、`onboarding.*`、`chat.*`、`projects.*`、`image.*`、`agent.*`、`updates.*`、`companions.*` 等。
- 同含义的“复制/取消/返回”可共用；上下文不同的同文字符串可以分别命名，不能只按中文内容去重。
- Key 不是中文原文，不用行号、随机编号或运行时生成的长句。
- 参数使用 .NET 编号占位符 `{0}`、`{1}`，本版不引入 ICU、命名参数或新的模板库。
- 固定文本使用 Get，含格式参数的模板使用 Format；原生大括号只在 Format 模板中按 `{{`/`}}` 转义，不能对所有 JSON 字符串机械转义。
- 换行使用 JSON `\n`/`\r\n` 表达；是否保留 Windows 换行以原调用点显示/复制行为为准，不全局统一。
- 不允许重复 Key、非字符串值、空白 Key 或缺失引用；有意使用空字符串必须在清单中说明。
- 不设置英文默认文案，不做外部语言包覆盖；修改源 JSON 需要重新构建应用。

## 4. 读取、格式化与失败处理合同

拟新增 `src/TextResources.cs`，同一文件包含两个明确职责：

| 接口 | 合同 |
| --- | --- |
| `TextCatalog.Load(Stream stream)` | 读取只读目录，验证顶层和值类型；用于独立测试和生产载入；输入流生命周期由调用方管理 |
| `TextCatalog.Get(string key)` | 精确大小写匹配；未知 Key 返回 `[key]` 并记录一次诊断，不导致整页崩溃 |
| `TextCatalog.Format(string key, params object[] args)` | 使用 CurrentCulture 格式化，保持原插值/拼接的环境习惯 |
| `TextCatalog.Format(IFormatProvider provider, string key, params object[] args)` | 保留原调用点明确指定的区域格式，不改变原有 InvariantCulture 行为 |
| `TextResources.Get(string key)` | 使用进程内默认的内嵌中文目录，供所有调用点使用 |
| `TextResources.Format(...)` | 与上面两个 Format 重载同签名，只负责转发到默认目录 |

`TextCatalog` 不公开可变 Dictionary；Default 初始化一次，读取线程安全。复用现有 `JavaScriptSerializer` 及 .NET 10 兼容层；生产 JSON 由严格构建检查保证无重复 Key，不重写通用 JSON 解析器。

统一逻辑资源名为 `Dafeiyu.Locales.zh_CN.json`。使用定义 TextResources 的程序集读取资源，不依赖工作目录、绝对路径或 UI 入口程序集。WPF 应用和独立测试可能分别编译同一文件，必须各自正确登记资源。

文字目录只解析一次，不在刷新进度、定时器或每次 Get 时重新读文件。未来可扩展语言选择，本版不提前实现动态切换/资源事件系统。

资源整体缺失/损坏属于构建或安装缺陷，初始化抛出可定位的 InvalidDataException，由现有启动错误处理显示明确说明；允许保留一条不依赖资源的兜底中文“界面文字资源不可用，请修复安装。”并登记排除。不能静默加载空目录而把整页显示成 Key。

格式参数不足或模板损坏属于代码缺陷：单元/CI 检查必须失败；不通过吞异常并显示错误句子掩盖问题。发布验收不得出现缺失 Key 标记或格式异常。

## 5. 文案与程序行为分离

已观察到的高风险位置及处理方式：

| 文件 | 现状 | 本次最小处理 |
| --- | --- | --- |
| src/AboutPanel.cs | 根据“可用/不可用”设置颜色 | 状态值控制样式，资源只负责显示；不引入新的全局状态框架 |
| src/ImageGenerationSettings.cs | “服务默认”文本同时决定是否发送参数 | 使用明确的默认选项身份/值，显示从 JSON 获取；自定义输入行为保留 |
| src/ConnectionSettingsPanel.cs | 根据“朗读测试完成。”判断成功 | 让该调用点使用真实成功结果；不得把成功判据改成比较另一种显示字符串 |
| src/ReplyFiles.cs | 根据菜单标题决定启用 | 菜单动作/捕获的条目引用决定启用，标题独立本地化 |
| src/ShellMenus.cs、ChatAppShell.cs | 中文分组既参与显示也参与筛选 | 分开稳定组标识和标题；若旧标识被保存，保留兼容值 |
| src/BrowserPane.cs | 用是否含“下载”判断提示覆盖 | 用当前提示原因/状态判断，保留下载提示优先级 |
| src/ProjectSidebar.cs | 用标题是否含“分区”选择说明 | 调用方传递明确的对话框用途，避免从标题反推功能 |
| src/Settings.cs | 根据中文默认服务名识别旧值 | 保留旧数据识别文本，显示标签独立；不全量重命名用户数据 |
| desktop/.../OfficialCompanionProgressView.cs | 按中文路由说明推导标签 | 有结构化路由数据则使用该数据；没有则保留内部稳定值并只抽取最终显示，不能扩展网络实现 |

查找范围不止本表，任务 1 的清单必须覆盖新增发现。以上判断修整仅为完成本次文案抽取所需，不进行无关业务重构。

---

## 6. 文件职责与产物

所有路径相对实施仓库根；本文件存放在外层工作区，不代表应直接修改当前主线程正在使用的审查副本。

| 路径 | 操作及职责 |
| --- | --- |
| assets/locales/zh_CN.json | 新建，唯一生产中文资源源文件 |
| src/TextResources.cs | 新建，目录加载、只读查询、格式化、默认入口 |
| tools/verify_localization.py | 新建，源码候选清点、Key/模板检查、迁移覆盖检查；仅使用 Python 标准库 |
| docs/localization/migration-inventory.jsonl | 新建，每个候选的基线、分类、用途、目标 Key 或保留理由 |
| tests/fixtures/localization/text-baseline.json | 新建，迁移前文字及模板的摘要/期望，防止迁移时无意改文案 |
| tests/LocalizationTests.cs | 新建，共享读取/格式/并发测试，WPF 与 portable 复用 |
| tests/LocalizationUiTests.cs | 新建，WPF 关键显示和文字/行为分离回归 |
| desktop/DeskPet.Desktop.Tests/LocalizationUiChecks.cs | 新建，Avalonia 对应检查 |
| Build.ps1 | 增加资源、测试目标及所有相关独立测试编译源登记 |
| desktop/DeskPet.Core/DeskPet.Core.csproj | 链接共享源并嵌入同一份 JSON |
| desktop/DeskPet.Tests/{DeskPet.Tests.csproj,PortableTests.cs} | 登记共享测试和专用入口/default suite |
| desktop/DeskPet.Desktop.Tests/Program.cs、工程文件 | 将 UI 检查加入默认入口，提供专用定位入口 |
| .github/workflows/check.yml、macos.yml | 登记新增检查，不删/跳过原有测试 |
| docs/localization/zh-CN-resource-guide.md | 新建，维护规范、允许保留项及添加词条步骤 |
| docs/localization/zh-CN-migration-validation.md | 新建，最终 SHA、命令、结果、截图和未覆盖边界 |

界面及业务代码按任务 1 产出的清单修改。大量文件将发生小范围字符串替换，不顺带格式化整个文件、不重命名控件 Name、不改变测试定位标识。

## 7. 实施任务

### 任务 1：冻结范围并建立迁移清单（2～3 小时）

**Files:** tools/verify_localization.py、docs/localization/migration-inventory.jsonl、tests/fixtures/localization/text-baseline.json。

**Consumes:** 实施开始时确认的提交及生产 C# 文件。
**Produces:** 带 baseline_sha 的清单；每条具有 file、symbol_or_context、literal_hash、occurrence、kind、decision、resource_key/exclusion_reason、reviewed。

- [ ] 确认最新 dev、工作区状态及当前主线程任务；使用独立实施工作区，不能覆盖或混入其他任务改动。
- [ ] 编写候选扫描：排除注释，识别普通/转义/逐字/插值字符串；遇到不支持的语法显式报告人工复核，不将简单正则数量当完成率。
- [ ] 补充审阅 Ui.Text/Button/Field、Title/Content/Header/ToolTip/PlaceholderText/辅助名称等显示入口，发现纯英文固定文案和间接变量来源；图标符号及技术标识逐项记录排除。
- [ ] 使用含注释、中文转义、插值、大括号、同文不同用途的小夹具验证扫描，不读取用户配置和聊天数据。
- [ ] 为生产代码的每个候选选择 migrate/exclude；排除项必须有具体原因，不能使用整目录全部忽略绕过审计。
- [ ] 从迁移前提交捕获实际文字/模板期望及摘要，不从迁移后的 JSON 反向生成“预期”。保留现有关键 UI 测试中的独立中文期望。
- [ ] 列出全部中文参与逻辑判断的位置，加入任务 3。输出按模块的实际条目数、文件数、未分类数。
- [ ] 检查清单未分类数为 0；人工复核全部排除类别和主要用户入口；形成单独可审查提交。

建议命令合同（脚本将在本任务创建，本轮尚不可执行）：

```powershell
python tools/verify_localization.py inventory --baseline <实施基线SHA>
python tools/verify_localization.py check --phase inventory
```

验收：扫描夹具通过，候选全部归类；数量变化能解释；没有移动生产文字。

### 任务 2：实现资源读取并打通两端构建（2～4 小时）

**Files:** src/TextResources.cs、assets/locales/zh_CN.json、tests/LocalizationTests.cs、Build.ps1、Core/Tests csproj、PortableTests.cs。

**Consumes:** 第 3～4 节合同。
**Produces:** TextResources.Get/Format、内嵌资源名、`LocalizationTests` 目标、portable `--localization-only` 及默认测试登记。

- [ ] 写失败测试：读取中文/换行/转义、未知 Key、错误 JSON/值类型、资源缺失、格式参数不足、并发读取。
- [ ] 运行新增测试目标，确认因尚未实现读取或资源未登记而失败，保留可定位日志。
- [ ] 实现一次性读取和不可变目录，使用现有序列化层，按第 4 节处理错误。
- [ ] 登记所有 WPF 独立测试需要的共享源与资源；不能仅让 App 目标通过。Core 使用相同逻辑资源名嵌入同一 JSON。
- [ ] 验证 Get 原样返回含大括号文字；Format 正确处理 `{0}`、`{{`、换行、null、数字和显式 IFormatProvider；不修改线程 Culture。
- [ ] 从源码目录之外运行测试，并发读取 10,000 次无状态变化，目录只解析一次。
- [ ] Windows 与 portable 新测试通过后形成独立提交。

### 任务 3：解除显示文字与行为的耦合（2～3 小时）

**Files:** 第 5 节所列文件及任务 1 新发现的位置；tests/LocalizationUiTests.cs 和相关既有测试。

**Consumes:** 稳定动作/状态和 TextResources；**Produces:** 无需读取显示文字来决定业务行为的相关调用点。

- [ ] 对每个已发现耦合点先补失败测试：在不改业务值的条件下改变显示内容，原功能仍应成立。
- [ ] 最小修改状态/动作映射，不将枚举、JSON Key 或 UI 文案写入原持久化身份字段。
- [ ] 分别验证：可用性颜色、服务默认参数省略、朗读成功、文件菜单启用、菜单分组、下载错误优先级、项目说明。
- [ ] 加载原中文默认名称/自定义名称的旧配置，确认识别和保存往返不改用户字段。
- [ ] 重新搜索中文比较/Contains/StartsWith，逐条确认仍存在的属于兼容值或内容处理；不能通过“比较 TextResources.Get(...)”伪装完成解耦。
- [ ] 相关目标通过后形成独立提交。

### 任务 4：迁移 WPF 界面与使用引导（4～6 小时）

**Files:** 清单中 src 的 UI 类；重点 Ui.cs、SettingsPage/SettingsWindow、WorkSettingsPanel、OnboardingLessons/OnboardingWindow、ShellMenus/TrayContextMenu、ConversationComposer、ProjectSidebar 系列、图像页面及卡片。

**Consumes:** 任务 1 的 migrate 清单、任务 2 API、任务 3 行为分离结果。
**Produces:** WPF 范围全部调用点替换与对应 JSON Key。

- [ ] 以设置/引导、聊天/项目、图像/任务、角色/关于四组小批次迁移，每组同步清单和 JSON，不运行全文件格式化。
- [ ] 普通文案使用 Get；动态完整句使用 Format；不改变动作回调、控件 Name、宽高、主题资源或加载顺序。
- [ ] 对原为 const/属性默认值的文案逐项处理编译期约束；不能随意将默认参数改为非编译期表达式。
- [ ] 每组运行其既有功能测试和新增 UI 显示断言，确认文字与基线一致后再迁移下一组。
- [ ] 复核 Tooltip、确认框、空列表、无配置、错误页和辅助功能文字，不只检查主窗口正常状态。
- [ ] 每个可独立验证的小批次提交；WPF 清单中待迁移数为 0 才结束本任务。

### 任务 5：迁移 Avalonia 界面（3～5 小时）

**Files:** 清单中 desktop/DeskPet.Desktop 生产 UI；重点 Ui.cs、ChatWindow、SettingsWindow、GuideWindow、WorkSettingsView、ImageWorkspacePage、ImageProviderEditor、GeneratedImageCard/GroupCard、ImageTaskCenterPage；新增 LocalizationUiChecks。

**Consumes:** 同一份 JSON 和 TextResources，不复制一套读取器。
**Produces:** Avalonia 范围全部调用点替换及默认 UI 测试登记。

- [ ] 对与 WPF 同含义文案复用 Key；原本不同措辞保留原文，不能为了共用 Key 顺便统一文字。
- [ ] 分设置/引导、聊天/项目、图像/角色批次替换；各批次先补独立文字显示期望再改代码。
- [ ] 保留原布局、PlaceholderText、菜单快捷键和可访问名称，不依赖中文标题定位控件。
- [ ] 在默认桌面 UI 测试和专用 `--localization-ui-only` 中登记检查；仅添加开关而不默认执行不算登记完成。
- [ ] 检查浅/深主题、390 DIP 窄窗口、不同状态下的提示，关闭重开页面无异常。
- [ ] 范围内待迁移数为 0，相关测试通过后提交。

### 任务 6：迁移共享提示与动态文字（3～4 小时）

**Files:** 清单中共享业务提示；重点 ImageGenerationClient、ImageTaskCenterState、ImageGenerationMotion、GeneratedImageStore、下载/更新/导入错误路径、会话与权限提示。

**Consumes:** 用户展示路径分类和格式化合同；**Produces:** 不改变错误类型、业务语义和记录结构的资源化提示。

- [ ] 对原始动态表达式保留独立预期，覆盖空值、0/1/多项、中文路径、引号、换行和普通大括号。
- [ ] 使用完整模板替换拼接，保留错误类型、HTTP/重试逻辑、阶段、异常内部链和取消语义；不捕获后重新包装导致丢堆栈。
- [ ] 在 zh-CN、en-US、de-DE 的 CurrentCulture 下比较迁移前后的数字/日期表达；原明确 InvariantCulture 的调用点继续相同。
- [ ] 运行使用错误片段断言的既有测试，保留关键中文期望，不将所有断言机械换成调用相同资源函数。
- [ ] 已持久化的旧中文 Error/名称原样可读；新提示仍保存原有最终中文，不在本版引入错误码迁移或历史批量改写。
- [ ] 核查 Agent 提示词和工具 schema 没有因抽取而变化；允许保留内容与清单一致后提交。

### 任务 7：完整性审计与安装包验证（2～4 小时）

**Files:** verify_localization.py、迁移清单/基线、Build.ps1、相关 csproj、check.yml/macos.yml、必要的现有打包测试登记。

**Consumes:** 所有迁移完成的调用点和资源。
**Produces:** 可持续执行的完整性门禁；WPF/portable/安装包资源一致证据。

- [ ] 严格检查 JSON：重复 Key、值类型、Key 格式、UTF-8、无效控制字符、空字符串例外。
- [ ] 检查源码引用存在、模板占位参数一致、无未解释的未引用 Key；禁止无依据批量删除或自动补中文兜底。
- [ ] 审计所有生产候选：已迁移引用能追溯到清单；新增中文需要分类；排除项需匹配具体文件/上下文/摘要而非整目录放行。
- [ ] 对资源变更后的文本做基线比较：静态文字一致，动态模板逐个验证参数位置；任何措辞差异先修正，不默认接受。
- [ ] 在 Windows App、独立测试、Core DLL、Avalonia 成品中检查实际逻辑资源存在，加载后词条值与源 JSON 一致。
- [ ] 对应用便携/精简包和安装器所含应用验证：复制成品到不含源码/语言文件的临时目录运行正常；不能依靠开发工作目录读取 JSON。
- [ ] CI 登记检查和测试，确认 Windows/macOS 两条构建链均涵盖资源；仅检查源码中有 EmbeddedResource 声明不算成品验证。
- [ ] 更新审计与打包测试后形成独立提交。

建议命令合同：

```powershell
python tools/verify_localization.py check --phase complete
python tools/verify_localization.py check --phase complete --report work/localization-audit.json
```

### 任务 8：最终回归与交付记录（4～8 小时）

**Files:** docs/localization/zh-CN-resource-guide.md、zh-CN-migration-validation.md；必要时同步 README 开发说明，不改变版本号。

**Consumes:** 任务 1～7 全部通过的提交。
**Produces:** 第 8 节验收矩阵结果、最终 SHA 与维护说明。

- [ ] 在隔离配置中构建并运行新资源测试、相关既有功能测试及完整共享核心测试；不修改日常配置。
- [ ] 串行执行占用原生鼠标/焦点的 UI 测试，避免与主线程其他 UI 验证争抢桌面。
- [ ] 双端检查主窗口、设置、引导、菜单、图像/任务、角色和错误状态；保存关键截图并与迁移前同状态对照。
- [ ] 执行下面矩阵中全部必需项，记录失败原因、实际修复和重跑范围；不将未执行或环境阻塞写成通过。
- [ ] 检查 git diff 仅包含文案资源化及必要耦合修正，无提示词/布局/权限/网络行为变更；检查无残留缺失 Key 标记。
- [ ] 编写维护说明：如何增加 Key、如何使用 Format、如何添加排除理由、如何运行检查；明确本版只有中文。
- [ ] 汇总实际文件数、迁移条目数、保留条目数、测试命令/结果和平台边界；完成本地提交后交付，不自动推送/发版。

## 8. 详细测试与验收清单

| ID | 检查内容 | 通过标准 | 归属 |
| --- | --- | --- | --- |
| L01 | 候选清点 | 注释不误判，普通/逐字/插值字符串有处理，不支持语法显式列出，未分类为 0 | 任务1 |
| L02 | JSON 格式与编码 | UTF-8 无 BOM，无重复/空白 Key，无非字符串值，例外空值有说明 | 任务2/7 |
| L03 | 静态读取 | 中文、引号、反斜杠、换行、普通大括号与基线一致 | 任务2 |
| L04 | 模板格式化 | 参数位置和重复使用正确；缺参数/错误模板测试能捕获；普通文本不误做 Format | 任务2/6 |
| L05 | 区域格式 | 三种 Culture 下均与迁移前表达式相同，不改变当前线程文化 | 任务6 |
| L06 | 错误资源 | 缺失/损坏资源有明确启动错误；单个未知 Key 不崩溃且产生诊断；正常成品无未知 Key | 任务2/7 |
| L07 | 线程和开销 | 多线程 10,000 次读取正确，目录只载入一次，无每次磁盘访问 | 任务2 |
| L08 | 状态与文字解耦 | 修改显示标签不改变可用性样式、菜单分组、参数默认值和成功判据 | 任务3 |
| L09 | 文件菜单/浏览器 | 文件菜单启用条件及下载错误优先级保持原行为 | 任务3 |
| L10 | 老配置 | 中文默认名/自定义名加载保存往返不变，不误重命名用户服务 | 任务3/6 |
| L11 | WPF 完整性 | 界面、确认/错误/空状态、Tooltip/辅助文字均完成范围内替换 | 任务4 |
| L12 | Avalonia 完整性 | 同上，使用同一资源源文件；原本不同的措辞不被强制统一 | 任务5 |
| L13 | 动态提示 | 空值、中文路径、0/1/多项、引号、大括号、换行输出一致 | 任务6 |
| L14 | 异常与取消 | 原类型、阶段、错误片段、内部异常和取消语义不变，不新增网络/重试 | 任务6 |
| L15 | 历史兼容 | 旧会话/任务/配置直接可用，不批量改历史中文，不写入新资源 Key 替代原身份字段 | 任务6 |
| L16 | Agent 行为边界 | 模型系统提示词、工具 schema、人格正文等排除项内容与基线一致 | 任务1/6/7 |
| L17 | 引用与遗漏 | 所有 Key 引用存在，全部候选可追溯；未迁移/未分类/无理由排除均为 0 | 任务7 |
| L18 | 文案保持 | 基线对比无非预期措辞、标点、空白、换行变化；重要 UI 测试保留独立期望 | 任务7/8 |
| L19 | 构建一致 | App、独立测试、共享 Core、Avalonia 成品嵌入相同逻辑资源与词条 | 任务2/7 |
| L20 | 成品独立运行 | 无源码目录/外置JSON时，便携/精简/安装应用均显示正常 | 任务7 |
| L21 | 双端 UI | 浅深主题、390 DIP及常规宽度布局与基线一致；页面可关闭重开 | 任务4/5/8 |
| L22 | 核心流程 | 聊天/项目、设置保存、配置引导、图像任务、角色管理、关于/更新对应既有测试通过 | 任务8 |
| L23 | CI 门禁 | 新检查进入默认测试/CI；Windows与macOS构建结果分开记录 | 任务7/8 |
| L24 | 改动范围 | 未改版本、未发请求到付费模型、未变权限/布局/提示词、未覆盖其他任务改动 | 全程/任务8 |

以上全部为必须项。中文残留不要求“源码中一个汉字都没有”，要求“范围内文案没有遗漏，范围外每项有依据”。

### 8.1 预期测试命令

以下在任务登记完成后执行；新增目标/参数在当前版本尚不存在。PowerShell 在实施仓库根执行，不在正在进行其他工作的审查副本直接运行。

```powershell
python tools/verify_localization.py check --phase complete --report work/localization-audit.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target LocalizationUiTests
dotnet build desktop/DeskPet.Tests/DeskPet.Tests.csproj -c Release -m:1
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --localization-only
dotnet desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --report-json work/localization-portable-report.json
dotnet build desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj -c Release -m:1
dotnet desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll --localization-ui-only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Target App
```

预期结果：命令退出码为 0；完整共享报告无失败；审计无缺失引用/未归类候选/文案差异；UI无缺失Key/格式异常。

既有回归至少包含以下**与实际修改匹配的目标**：ThemeTests、MenuSmoke、AppShellTests、SettingsNavigationSmoke、WorkSettingsTests、OnboardingSmoke、OnboardingModelSmoke、ProviderTests、ProviderUiSmoke、ProjectCatalogTests、SidebarActionTests、BrowserPaneTests、ImageGenerationBackendTests、ImageGenerationUiTests、ImageTaskCenterUiTests、ImageTaskCenterLifecycleTests、GeneratedImageGroupTests、HistoryTests、ReleaseIdentityTests。不将这些目标全部机械跑一遍就称覆盖完整，其他被修改模块的现有目标也必须补入清单。

原生浏览器/UI测试使用现有要求的参数和环境，不擅自跳过 native 检查。涉及真实麦克风、远程语音或付费生成的目标只运行离线夹具/编译部分；另列未执行的真实服务边界，本次文案重构不需要付费调用。

macOS 通过现有 CI 执行共享核心与 Avalonia 默认检查，Intel/Apple Silicon分别记录。若本轮未获推送授权，则仅完成本地验证并明确标注远端未运行，不能为了取得CI结果自动推送。

## 9. 工时、里程碑与风险处理

| 里程碑 | 预计工时 | 可检查产物 |
| --- | ---: | --- |
| 任务1：范围/清单 | 2～3小时 | 真实分类统计、迁移前基线 |
| 任务2：资源基础 | 2～4小时 | 两种运行时均可读取同一资源 |
| 任务3：解除耦合 | 2～3小时 | 状态不依赖中文显示的回归 |
| 任务4：WPF迁移 | 4～6小时 | WPF范围待迁移为0 |
| 任务5：Avalonia迁移 | 3～5小时 | Avalonia范围待迁移为0 |
| 任务6：共享提示 | 3～4小时 | 动态句子/错误/历史兼容通过 |
| 任务7：审计/成品 | 2～4小时 | 覆盖报告和独立成品验证 |
| 任务8：最终回归 | 4～8小时 | 验收矩阵与交付说明 |
| 总计 | **22～37小时** | **约3～5个工作日有效工作量** |

执行顺序：1→2→3→4→5→6→7→8。每个迁移批次保持可编译且资源引用完整；本计划不要求并行代理执行。

若任务 1 发现候选明显多于基线、需要改动持久化协议，或仅为抽取文字就必须大改某个子系统，应先报告范围变化并重新估时，不在本次重构中顺手实施。原有测试失败先区分基线失败、环境问题与本次引入的回归，保留证据，不删除断言或无上限增加等待。

## 10. 回滚与最终交付

- 每个里程碑形成独立提交；资源 Key 的增加和使用它的调用点在同一批次交付，不留下引用缺口。
- 若出现功能回归，回退对应迁移提交及相关新增 Key，不能删除整个共享资源导致其他模块失效。
- 本次不迁移用户数据格式，因此回退应用不应需要回退配置或聊天数据。
- 最终交付包含：源资源、共享读取器、完整迁移、检查脚本、测试登记、维护说明、验证记录。
- 交付报告明确：最终SHA、资源条目数、修改文件数、排除项数、本地/远端各自结果、尚未验证平台。
- 推送、合并、版本号和 Release 由后续明确任务决定，本计划不自动执行。

## 11. 本计划自查

- [x] 范围覆盖两套UI、共享提示、构建和成品；明确排除模型内容、用户数据和既有素材。
- [x] TextResources API、资源名、文件名和测试入口前后一致；所有新增命令注明尚未实现。
- [x] 五类主要风险都有负责的实施任务和可检查验收项。
- [x] 工时各项相加为22～37小时；历史粗统计未冒充最新精准词条数。
- [x] 本轮只写计划文档，未实施上述任何代码/测试修改。
