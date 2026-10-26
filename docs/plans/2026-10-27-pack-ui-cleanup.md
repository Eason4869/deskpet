# 0.11.1-dev.x implementation plan

Goal: independent official material versions, verified reuse, safe historical storage maintenance and accurate UI copy, based on dev c2c5f61 (0.11.0-dev.3).

Architecture: preserve immutable official installations, package identity and existing integrity validation. Keep package filenames tied to material versions for legacy readers. Maintenance must fail closed when shared-reader ownership cannot be proven. No release creation, no main push.

Approved design: prior chat's 2026-10-26 character-pack-and-ui-cleanup design. Its local file was removed during workspace reorganization; this implementation record preserves its decisions: per-role material versions/minimum application version, distinct release names, deterministic ZIPs, no receipt rewriting, legacy migration may download once, current + previous + active/unknown versions protected, seven-day retirement grace, custom-pack cleanup deferred, all 13 locales updated via feature keys, no art changes.

## Review focus

- Higher historical package versions must not block real future material changes.
- Equal SHA with conflicting package identity must not become a successful update result.
- Failed pointer commits must preserve both old current and old previous.
- Old clients and other sessions must never lose resources due to missing new leases.
- Version tags must not create GitHub Releases.

## Task 1 — 0.11.1-dev.1

Files: tools/build_official_companions.py, assets/official/companion-versions.json, src/OfficialCompanion{Models,Feed,Storage,Downloads}.cs, tests/OfficialCompanion{AuthoringTests.py,Tests.cs}, 13 locale catalogs, both UI entry points, release/character-pack documentation, ReleaseInfo and changelogs.

- [x] Add failing authoring tests for independent release/material/minimum versions and semantic change gates; add C# regressions for content reuse/conflicts and 1024 file inventory.
- [x] Implement shared filename/content decisions, immutable receipt reuse and independent version registry.
- [x] Retire obsolete active copy, add accurate multilingual capability/download copy; update localization provenance.
- [x] Run directed Python/shared/WPF/UI and localization checks; review before integration.
- [x] Commit, fast-forward dev without overwriting concurrent changes, tag v0.11.1-dev.1; verify remote refs and CI. No Release.

## Task 2 — 0.11.1-dev.2

Files: official storage transaction/maintenance policy, cache service and both cache views, tests and docs.

- [x] Add failing transaction tests preserving current/previous on commit failure and restart recovery.
- [x] Implement transaction metadata and safe scan protection; manual cleanup only where exclusive shared-reader ownership can be proved.
- [x] Verify malformed paths, changed scans, corrupt pointers, unknown/old reader protection, migration and failure counts.
- [x] Review and commit version2 implementation and CI repairs. Delivery target: dev and v0.11.1-dev.2, with consolidated final CI permitted by the user; no Release.

## Task 3 — 0.11.1-dev.3

- [x] Enable automatic maintenance only with the same verified exclusive policy and seven-day grace; test failures do not turn a successful install into failure.
- [x] Measure repeated validation before changing performance behavior; document evidence and any safe deferral.
- [x] Complete local build/UI/localization regression and final review. Delivery target: push dev, verify final CI, then tag both remaining version commits; no Release.

Execution is authorized by the user's request; no extra approval pause. Each phase records concrete verification and rulings in work/pack-ui-cleanup/progress.md. A safety condition that cannot be proven stays protected and is reported explicitly.


## Implementation rulings

- dev.1 shipped to dev at 803e9d9 with v0.11.1-dev.1. Two actual full builds across app release numbers produce identical five ZIP digests, all five native ZIP/install/final-root checks passed.
- dev.2 only reclaims newly managed v2 cache generations with schema2 receipts; legacy v-* roots remain protected because old processes lack leases. Compact hash-prefix + random generation keeps path lengths comparable; complete digests stay in verified receipts. Managed readers retain leases through service disposal.
- Publication/readers wait for the lifecycle lock with cancellation and a 90-second sharing-violation bound; maintenance scans skip busy roles. Native Windows path pinning uses extended absolute paths for nested resource trees.

- dev.3 uses one verified read per normal managed offline query (measured2 before/1 after on the same fixture); every new query still rejects byte tampering. Automatic history sweeps are asynchronous, history-only, serialized per service, cancelable and drained with a five-second shutdown bound. Partial/unverifiable leftovers stay protected rather than expanding deletion authority.
- CI dev.1 revealed stale whole-catalog byte pins. Versioned pins were explicitly refreshed without rewriting historical source hashes; a Python preflight now catches raw-byte drift before native test loops. Every dev push is inspected and CI repaired; the user permits consolidated final CI. Remaining version tags follow final validation, historical tags remain immutable.

## 最终范围与验收记录

用户已授权逐版实施、推送 dev 并打标签；不推送 main，不创建 Release。附件中的“未授权实施”属于原计划状态，已由用户后续明确指令更新。

| 版本 | 最终内容 | 验收依据 |
| --- | --- | --- |
| 0.11.1-dev.1 | 独立素材版本、摘要复用、并发下载去重、1024 文件清单上限、踉跄能力说明及两端下载引导修正 | 两个应用版本实际构建的五份 ZIP 摘要均相同；两组五角色原生完整包安装验证通过 |
| 0.11.1-dev.2 | 安装事务、前版恢复、历史副本手动清理、13 语言缓存与列表移除文案 | 生命周期定向 19 项；共享官方包 119 项；WPF 官方包 120 项；两端缓存与伙伴界面回归通过 |
| 0.11.1-dev.3 | 安装后后台自动维护、取消与退出收尾、每次查询完整校验由两遍降为一遍 | 自动维护及校验优化定向 7 项；共享官方包 126 项、WPF 127 项；后续字节篡改仍被拒绝 |

额外发现并修复：独立更新器编译不应依赖角色包维护模块；macOS/Linux 的文件锁占用错误码应进入有界重试；本系列新增文案需要同步嵌入语言包的字节快照并在 CI 提前检查。

安全边界：受管新格式保留 current、一个恢复前版、正在使用和未知状态副本；退役七天后才具备回收资格。旧格式安装、自定义包历史及无法复验的部分副本不在本轮删除范围，部分读租约需退出进程后释放。不会承诺磁盘始终只剩两份。

CI 记录：dev.1 已有标签保持不变，其语言快照失败由后继版本修复。dev.2 的 64e3255 已通过完整 macOS 两架构 CI 和 Windows 全量测试循环；Windows 后续打包验收仍假定素材与应用同版本而失败，由 ff3d37a 修复。最终按用户许可将修复合入 dev.3，集中验证完整 CI 后补齐 dev.2/dev.3 标签，不重写已发布标签。最终交付状态以远端引用与 GitHub Checks 为准。

导出源码打包继续逐角色校验 registry 与 payload 摘要，但不会误借用父目录的 Git 历史；CI 的显式历史门禁仍要求完整 Git checkout，浅克隆不能以源码导出模式绕过。新增边界测试 6 项和实际五 ZIP 的新版打包断言均通过。


最终打包验收补充：旧测试还要求引导简介包含固定步骤总数，而当前简介已取消这一冗余表达。编译应用与文档实际均为 134 步、标题逐项完全一致；fe443ee 将验收改为检查连续编号及实际编译标题，保留严格对照。该修复也纳入 dev.2 源码提交。dev.3 本地完整 PackagingSmoke 已从干净源码跑至结束：五角色原生安装、实际更新器应用、教程与清单、嵌入资源、README 图片和准确源码归档均通过，再推送修复并验证完整 CI。
