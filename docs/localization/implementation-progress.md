# SDD ledger — plan: docs/localization/planning/2026-10-23-localization-and-agent-background-jobs-plan.md

计划：外层 `docs/superpowers/plans/2026-10-23-localization-and-agent-background-jobs-plan.md` 的 S0–S7；后台任务明确不做。

- 目标：0.10.5-dev.1（用户确认将 0.10.4 留给正式热修复）；用户已授权版本目标，覆盖原计划“不改版本”的限制。
- 基线：远端 dev `4944d11416fec7b494a8eb8ac3c4b8376e1100fd`，2026-10-23 fetch 与 ls-remote 一致。
- 工作区：`E:/workspace/deskpet/localization-0.10.4`，分支 `codex/localization-v0.10.4-dev.1`。
- S0：隔离目录已建立，未修改原有副本；本机 SDK 10.0.203，正在隔离目录补齐 10.0.402。
- Ruling: 原计划需要整体候选审计；使用 SDK 自带 Roslyn 精确提取 C# 语法与常量，避免正则处理插值/注释的误判。生产不新增运行时依赖；持续检查由标准库 Python 完成。
- Ruling: 本轮用户授权实施与目标版本，不推断为共享分支推送、合并或发布授权；先完成本地实现、验证与可审查成品。
- 任务：S1 清单 [ ]；S2 资源 [ ]；S3 解耦 [ ]；S4 WPF [ ]；S5 Avalonia [ ]；S6 共享 [ ]；S7 审计/验收 [ ]。

- 用户确认：角色恢复单独作为 main→0.10.4 正式紧急更新；JSON 重构顺延 0.10.5-dev.1，后台任务仍不做。
- main=5670d6087f2ab017dcf3f560d956466bcf8d78bc，与 dev 基线源码树一致。
- Framework 资源22项、缺资源真实宿主2项、解耦5项、新Avalonia语言UI12项通过；迁移后默认Avalonia2971断言通过（新增最终检查前的一轮）。
- WPF原生MenuSmoke沙箱SetCursorPos error5，桌面重跑发生焦点关闭失败；其余原生回归逐项运行，结果位于work/localization-validation。

## 继续实施：0.10.4 发布后

- 2026-10-23 核对远端：dev 仍为 `4944d11416fec7b494a8eb8ac3c4b8376e1100fd`，main/v0.10.4 为 `c544752c6b5c1f0913bdab52942fc701f928b296`。
- 本地检查点 `9a20366` 保存此前未提交迁移；集成提交 `a17c556` 合入正式热修复，保留 0.10.5-dev.1 / 0.10.5.0 版本身份。
- 分支改名为 `codex/localization-v0.10.5-dev.1`，继续使用原隔离目录，未覆盖其他工作区。
- S0 complete: 三份输入和统一计划归档到 planning；SDK 10.0.402 可用；后台设计缺口仍只约束后续后台阶段，本次不实施。
- S1 complete: 对原始 dev 提交直接扫描 4,937 个中文候选，未分类为 0；额外英语 UI 候选和 S3 已删除的比较点一并保留记录。
- S2/S3 regression: Framework missing-key diagnostic RED→GREEN；浏览器后续导航错误优先级 RED→GREEN；真实镜像失败转直连的路线身份 RED→GREEN，OfficialCompanionTests 78 passed。
- S4/S5/S6: 既有迁移继续保留；修正五个误分类展示/朗读路径；文案精确基线包含 4,347 个资源。
- S7 in progress: Python audit 9 tests passed，strict complete gate passed；WPF 主程序/更新器在独立目录通过资源 SHA256 和真实 Get；.NET Core 实际产物通过同样检查。
- Shared full regression: `work/dotnet/dotnet.exe desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll --report-json work/localization-validation/portable-report.json` → success, 8,203 passed, 0 failures。
- Ruling: 沿用既有实施记录作为主账本，S0–S7 的适配步骤直接记录于此，不重建或删除其他计划的 scratch 目录 — 避免重做已完成迁移 — 成本是与通用 task-start 工具格式略有差别。
- Ruling: 0.10.4 的进度气泡新文案按正式标签内容加入独立基线；被移除的旧通知词条保留并在 retired-keys.json 解释 — 不盲删旧基线 — 成本是保留一个暂时不再引用的词条。
- Ruling: 独立开发分支的远端推送/草稿 PR 仅用于本次开发验收；共享 dev/main 合并和 0.10.5-dev.1 发版留待明确发布授权 — 完成跨平台验证且不提前发布 — 成本是 CI 排队和产物下载时间。
- Final review: read-only independent reviewer inspected `c544752..4589edb`; no Critical findings; two Important findings accepted. CI artifact output mismatch fixed with `test_windows_artifact_probe_uses_the_built_application` RED→GREEN. Missing reverse provenance fixed with `test_referenced_new_resource_requires_independent_provenance` RED→GREEN; seven menu-group display keys now have explicit derived source records. Audit suite 11/11 and complete gate green (4,347 resources, 4,999 inventory records, 4,649 migrated / 350 excluded).
- Visual acceptance: nine WPF screenshot pairs against immutable 0.10.4 are pixel-identical after masking only authorized version identity / isolated fixture workspace path; four Avalonia screenshot pairs are fully pixel-identical, including 390-DIP chat in light/dark themes.
- Current native checks passed: LocalizationTests 4,370, missing-resource 2, UI decoupling 7, CompanionStartup 11, Theme 32, MenuSmoke and BrowserPaneTests. ProviderUiSmoke first audio playback attempt failed; isolated rerun 91 passed.
- Baseline classification: SettingsNavigationSmoke and OnboardingSmoke fail identically on released 0.10.4 and this branch (old picker identity/synchronous or guide-state expectations). Assertions and production behavior remain unchanged. Report these as known baseline failures, not successful tests or new localization regressions.
