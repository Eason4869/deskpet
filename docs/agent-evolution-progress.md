# Implementation ledger — plan: docs/plans/2026-10-18-agent-evolution.md

2026-10-18. User authorized implementation of the supplied plan, plus interactions during agent work, automatic action mapping, and Markdown delimiter behavior.

## Baseline and boundaries

- User changed baseline to latest main; remote initially dbe033140ab20b2e0016f24b7d5d1496e271ac9e; fast-forwarded during validation to 8c7c1f9a5d19fe02daefad29d8d17fb1151378d1 (release workflow-only changes).
- Active isolated worktree: E:/workspace/deskpet/agent-main-evolution, codex/main-agent-evolution. Earlier worktree remains preserved, inactive.
- Original checkout has four pre-existing documentation edits (34 insertions, 14 deletions). Copied verbatim into this worktree; originals untouched. No existing source edits.
- No push, release, real-account writes, paid model calls, installed-preview replacement, or application restart.
- Existing review reports describe historical results; no claim that their full suite passes on this branch.
- Baseline ComposerRenderingTests: 76 passed; work/agent-evolution/baseline-editor.log.

## Decisions and interfaces

- Ruling: fix the two explicitly requested interaction/editor regressions after baseline, before capability changes. They are independent of new execution privileges and provide regression coverage for later UI work.
- Capability snapshot from task 1 must feed authorization/runtime and context policy (tasks 2–5), settings (12) and guide (14).
- Authorization identity must bind task/project/call/payload; terminal, MCP and journal consume the same identity (2, 3, 7, 10).
- Runner cancellation and immutable snapshots feed child workers (8–9); recovery must not reuse approvals (10).
- OS isolation must be genuinely verified; unavailable sandbox mode stays disabled. Any limited-delivery decision must follow the supplied plan's explicit user gate.
- Guide updates happen after whole-product review, not before new runtime semantics stabilize.
- User decision (2026-10-18): "先完成每次确认模式，其余计划继续". Deliver confirmed current-account terminal first; sandboxed stays disabled. WindowsSandbox.exe absent. AppContainer work deferred because profiles/ACL preparation and independent reverse probes have not been authorized or validated. Official references: https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer and https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install .

## Current evidence

- Final Windows results: 121 / 121 targets passed; full/results.json. Initial failures and final reruns are retained separately.
- ComposerRenderingTests: 94; PetRuntimeTests: 22 including native mouse during busy work; review regressions: 33.
- Actual local coding loop: exit 7, file edit, exit 0; unrelated user fixture preserved.
- Independent review findings were reproduced and fixed. See agent-evolution-review.md for resolutions and agent-evolution-validation.md for limits and log locations.
- Shared .NET checks: 355. Avalonia headless: 1604. These ran on Windows, not native macOS.
- Guide: 130 stable steps; Detailed 58, first-run 77, model 56, menu 8, upgrade 44 checks passed.
- Windows PowerShell 5.1 App build passed in the separate agent-evolution-local output directory. Main program not launched.

## Tasks

0. Complete: verified main baseline, preserved prior checkout, isolated new branch.
1. Complete: explicit per-model capability resolution and schema/direct-entry gates. Typed server metadata precedence implemented; user override remains first.
2. Complete: asynchronous exact-payload approvals and actual WPF UI.
3. Complete: current-account confirm-each terminal, bounded/paged output, process Job, encrypted execution record.
4. User-approved limited delivery: sandboxed unavailable/disabled. Real native coding loop passed, genuine exit 7 before edit and exit 0 after edit; unrelated existing file preserved.
5. Complete: shared policy reserves, preserved complete history, explicit failed compaction, actual-request budget reporting.
6. Complete: explicit project trust, scoped instructions and lazy skill loading, no script execution.
7. Complete: explicit MCP UI, no auto-connect, independent credentials, generation-bound tools, HTTP JSON 2025-03-26 subset and real stdio lifecycle. SSE unsupported and explicitly reported.
8. Complete: shared non-UI task runner and protected event sinks.
9. Complete: real independent read-only workers. Independent review race and parent permission inheritance regressions reproduced then fixed.
10. Complete: encrypted side-effect journal, pending-reconciliation gate and user acknowledgement. Fault injection prevents repeated effects.
11. Complete: manual scoped project facts, edits/deletion and evidence-derived terminal summaries. No automatic long-term memory.
12. Complete: Windows configuration and explicit unavailable-platform labels; new scope/persistence tests included.
13. Complete: whole-product serial audit, independent review resolutions, targeted reruns, shared and headless regression.
14. Complete, last: updated 130-step guide, explicit non-executing approval exercise, MCP navigation, no automatic service tests.
15. Complete: separate local executable, helper, configuration, help, templates, manifest and validation report. No publication or preview replacement.
A. 94 checks passed: inline whitespace/newline boundaries, heading exit, trailing-newline retention; whole-product regression passed.
B. 22 checks passed including genuine mouse drag while busy, streamed reply preserving capture, resumed action, failure and cancellation. Native test requires interactive-desktop permissions; initial restricted run failed, authorized desktop run passed.

Shared .NET 10 regression: 355 checks, exit 0, no failures (`work/agent-evolution/portable.json`), on Windows. This is not native macOS acceptance. Initial --no-restore failed because the fresh checkout lacked assets; restore succeeded before testing.

Contract mapping: runner completed/failed map to existing persisted task complete/error; cancelled remains cancelled. Approval waiting and animation states route through the session runtime action; durable operation reconciliation is recorded separately. Capability sources and user overrides are per provider/model/endpoint/protocol. Source changes remain uncommitted in this isolated worktree.
