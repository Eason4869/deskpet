# 语言 JSON 重构实施记录

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
