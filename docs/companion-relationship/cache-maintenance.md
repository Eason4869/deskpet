# Cache maintenance

The About page's **Clear cache** action opens an owned dialog. It scans on a worker, shows actual byte totals for expired update packages and abandoned companion downloads, cleans eligible entries, and rescans. Closing, Cancel and Escape affect only the dialog. There is no disk-thumbnail category: current gallery thumbnails are in memory.

Settings, API keys, histories and embedded attachments, all original/generated/reference/exported images, installed companions and outfits, relationship state/settings/receipts, and project/source directories are outside the candidate whitelist. History clearing, preference reset and uninstall are separate operations and are not part of this implementation checkpoint.

## Candidate and concurrency boundary

- Only `<data>/updates/<32 lowercase hex>` and `<data>/official-companions/<known official id>/.stage-<32 hex>` or `.transfer-<32 hex>` may register as cache resources.
- `CacheResourceLease.Acquire(dataRoot, directory)` reserves a shared cross-process handle before creating the resource and records its filesystem identity. The updater holds it across download and pending-ready state; the helper also holds it across the parent handoff and application of the update. Companion downloading holds separate stage and transfer leases through unpacking, validation, publication and cleanup.
- Lock and ownership files live in `<data>/cache-maintenance`, outside published character packs. These small records remain after cleanup so concurrent clients never switch between different lock files for the same resource. They do not contain conversation or relationship data.
- `ProtectExisting` lets a validated helper protect a legacy job without enrolling its old directory as reclaimable. Unregistered old resources are conservatively excluded: their timestamps do not establish that an older, lease-unaware worker has stopped.
- An eligible resource and every entry must be at least seven days old. Scans snapshot filesystem identities, directory/file times, sizes, names and SHA-256 content hashes. Execution takes an exclusive resource lease and repeats the same checks. Active, changed, linked, unreadable or uncertain entries are skipped.
- Prepared updates with `job.json`, `ready`, staging or rollback directories stay protected unless the current helper saved an explicit completion proof. The proof binds the exact job and successful result receipt, original installation directory and target version. Cleanup verifies that the current installed executable is at least that version. A missing receipt, failure, downgrade, changed job/result or absent proof retains rollback.

## Deletion and results

Cleanup never recursively deletes a data root or searches globally for `.tmp`, `cache` or `temporary` names. It removes only files from the revalidated inventory. New entries appearing later are left behind and prevent empty-directory removal.

Windows pins directory ancestors without delete sharing. Files are rehashed through exclusive read/delete handles and removed through handle disposition; sharing-denied files remain. POSIX uses no-follow directory descriptors, advisory file locks and descriptor-relative `unlinkat`, preventing a changed ancestor from redirecting deletion outside the pinned tree. These locks coordinate application/managed clients; they are not a boundary against a malicious process already able to rewrite the user's data.

Filesystem identities use volume/file-index on Windows and device/inode on supported 64-bit POSIX platforms. Darwin x64 uses `fstat$INODE64`; Darwin arm64 uses `fstat`. This follows Apple's [`stat.h`](https://github.com/apple/darwin-xnu/blob/main/bsd/sys/stat.h) and [`cdefs.h`](https://github.com/apple/darwin-xnu/blob/main/bsd/sys/cdefs.h) ABI selection. Runtime validation on actual macOS remains a separate CI/platform acceptance item.

Results count bytes only after a file removal succeeds, completed directories separately, and skipped/failed candidates explicitly. Partial deletion is reported as partial even when some bytes were released. Repeating an old scan never counts previously removed bytes again. Scanning reports protected resources and scan failures; an empty result does not mean all disk caches were deleted.

## Consumer APIs and checks

Shared C# 5 APIs in `src/Maintenance`:

- `CacheMaintenanceService(dataRoot).ScanAsync()` / `CleanAsync(scan)` for UI workers; synchronous `Scan(utcNow)` / `Clean(scan, utcNow)` for controlled-clock callers.
- Immutable `CacheScan.Candidates`, category byte totals, protected/failed scan counts; `CacheCleanResult` contains `FreedBytes`, `DeletedItems`, `SkippedItems`, `FailedItems`.
- `CacheUpdateRetirement.RecordCompleted(dataRoot, resource, targetVersion)` is called only after successful updater application and result persistence. Proof-write failure conservatively retains the cache and does not prevent application restart.
- `CachePresentation` provides identical localized totals and results to WPF and Avalonia. Twelve new resource keys are present in all thirteen locales; existing values are preserved.

Focused runners are `Build.ps1 -Target CacheMaintenanceTests`, `Build.ps1 -Target CacheMaintenanceUiTests`, portable `--cache-maintenance-only`, and native `--cache-ui-only`. They use owned fixture directories, including real cross-process leases, junction replacements, copied-timestamp identity changes, content changes, active readers, partial I/O failures, update completion proof, actual UI cancellation and rescan. The existing update schedule and companion-download regressions additionally verify their production lease lifecycles. New checks are registered in Windows and macOS CI.
