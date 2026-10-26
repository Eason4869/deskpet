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

- [ ] Add failing authoring tests for independent release/material/minimum versions and semantic change gates; add C# regressions for content reuse/conflicts and 1024 file inventory.
- [ ] Implement shared filename/content decisions, immutable receipt reuse and independent version registry.
- [ ] Retire obsolete active copy, add accurate multilingual capability/download copy; update localization provenance.
- [ ] Run directed Python/shared/WPF/UI and localization checks; review before integration.
- [ ] Commit, fast-forward dev without overwriting concurrent changes, tag v0.11.1-dev.1; verify remote refs and CI. No Release.

## Task 2 — 0.11.1-dev.2

Files: official storage transaction/maintenance policy, cache service and both cache views, tests and docs.

- [ ] Add failing transaction tests preserving current/previous on commit failure and restart recovery.
- [ ] Implement transaction metadata and safe scan protection; manual cleanup only where exclusive shared-reader ownership can be proved.
- [ ] Verify malformed paths, changed scans, corrupt pointers, unknown/old reader protection, migration and failure counts.
- [ ] Review, commit and push dev, tag v0.11.1-dev.2, verify refs/CI, no Release.

## Task 3 — 0.11.1-dev.3, if required

- [ ] Enable automatic maintenance only with the same verified exclusive policy and seven-day grace; test failures do not turn a successful install into failure.
- [ ] Measure repeated validation before changing performance behavior; document evidence and any safe deferral.
- [ ] Complete build/UI/localization regression, review, push dev and tag the final planned version; no Release.

Execution is authorized by the user's request; no extra approval pause. Each phase records concrete verification and rulings in work/pack-ui-cleanup/progress.md. A safety condition that cannot be proven stays protected and is reported explicitly.


## Implementation rulings

- dev.1 shipped to dev at 803e9d9 with v0.11.1-dev.1. Two actual full builds across app release numbers produce identical five ZIP digests, all five native ZIP/install/final-root checks passed.
- dev.2 only reclaims newly managed v2 cache generations with schema2 receipts; legacy v-* roots remain protected because old processes lack leases. Compact hash-prefix + random generation keeps path lengths comparable; complete digests stay in verified receipts. Managed readers retain leases through service disposal.
- Publication/readers wait for the lifecycle lock with cancellation and a 90-second sharing-violation bound; maintenance scans skip busy roles. Native Windows path pinning uses extended absolute paths for nested resource trees.

- dev.3 uses one verified read per normal managed offline query (measured2 before/1 after on the same fixture); every new query still rejects byte tampering. Automatic history sweeps are asynchronous, history-only, serialized per service, cancelable and drained with a five-second shutdown bound. Partial/unverifiable leftovers stay protected rather than expanding deletion authority.
- CI dev.1 revealed stale whole-catalog byte pins. Versioned pins were explicitly refreshed without rewriting historical source hashes; a Python preflight now catches raw-byte drift before native test loops. Every dev push is inspected and CI repaired; final tags follow green source commits, historical tags remain immutable.
