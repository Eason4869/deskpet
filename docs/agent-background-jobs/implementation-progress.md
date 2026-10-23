# SDD ledger — plan: docs/localization/planning/2026-10-23-localization-and-agent-background-jobs-plan.md (S8–S17)

- Target: 0.10.5-dev.2, authorized by user after dev.1 merge/tag.
- Base: origin/dev cc868e16f62a7d1f35e225a6472cbd1ca2f540cb; v0.10.5-dev.1 resolves to the same commit.
- Isolated workspace: E:/workspace/deskpet/localization-0.10.4, branch codex/agent-background-jobs-v0.10.5-dev.2. Reuse existing linked worktree and isolated SDK; no other worktree changed.
- CI prerequisite: merged-dev Windows 39943648201 and macOS 39943648210 are in progress. PR application acceptance 39938690319 / 39938690309 was successful, but does not replace the user's requested post-merge CI check.
- S8: pending contract confirmation; S9–S17: not started. No production code/version changed for this phase.
- Source search: no referenced agent-background-jobs-design file in QQ download directory, workspace file catalog, or available Git history. Equivalent design proposal is design-draft.md; sections 3–5 explicitly identify proposed missing protocol choices.
- Pre-flight: S8 → S9/S10/S12 consumes immutable snapshots/owner/spec and exact tool contract; original spec is missing, so freeze only after the equivalent contract is confirmed or the original supplied.
- Pre-flight: S9 → S10/S13/S15 consumes exclusive data-directory lease and owner mutation gate; a second host must remain unable to write bindings while ordinary chat remains available.
- Pre-flight: S10/S11 → S12/S13/S16 consumes one process ownership transfer boundary; cancellation and exit tests must distinguish pre-/post-handoff and cleanup confirmation.
- Pre-flight: S12 → S15/S16 consumes persistent conversation identity; primary must not change on Build/Plan transitions, and get must bypass write-journal result truncation.
- Pre-flight: S13/S14 → S15/S16 retains WPF-first stage acceptance, then required Windows Avalonia support; macOS only shared logic/capability denial.
- Preparation: exported exact dev.1 tag bytes of zh_CN.json to E:/workspace/deskpet/exports/0.10.5-dev.1/zh_CN.json at user's request; 4,344 keys, SHA256 8b213b02142c7d7930e12b2d793254a1671d0153275767f9f18caad8c9690541.

- User confirmation 2026-10-23: accepted the equivalent design contract and authorized development under it. S8 contract frozen in design-draft.md; only functional implementation remains gated on merged-dev CI. Protocol choices in sections 3–5 are now explicit user-approved scope.

- CI prerequisite complete: merged-dev Windows 39943648201 and all four macOS jobs 39943648210 succeeded at cc868e1. Full run/job metadata and native logs archived under work/agent-jobs-validation/base-ci; accepted.json is the gate.
- Added user scope: avatar language placeholder above settings, only selected Simplified Chinese; no translation, language switching, online packs or config migration. AppShellTests missing-entry RED → GREEN 77 checks; actual menu screenshot work/permissions/avatar-language-menu.png inspected; audit 12/12 and strict localization gate green (4,346 keys).
- S8 in progress: first contract suite observed four expected runtime failures for missing settings/policy, not compiler errors. Independent global/project limits, copies, persistence and effective settings now pass 29 Framework checks; immutable job contracts and remaining regression checks still pending.
