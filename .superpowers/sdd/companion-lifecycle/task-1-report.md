# M1 shared relationship core report

Status: implemented and focused verification green. Scope is M1 only: no UI, runtime event adapters, maintenance operations, version changes, publication or subagents. Base: `f25db9c0447ebb746964a789eef19074779147e2`.

Implementation commit: `bb1344f9f32a5e603c5e762d6bbed2249f4f3d30`. This report is recorded in the following dedicated documentation commit, discoverable with `git log -1 --format=%H -- .superpowers/sdd/companion-lifecycle/task-1-report.md`.

## Files and behavior

- `src/Relationships/RelationshipIdentity.cs`: provenance-aware identity using the existing canonical picker normalizer; deterministic legacy mapping.
- `src/Relationships/RelationshipState.cs`: shared C# 5 DTOs, injected UTC/offset, snapshots and results. Persisted UTC values use integer .NET ticks, preserving submillisecond rollback/cooldown checks across Framework and portable restarts.
- `src/Relationships/RelationshipRules.cs`: score stages, category and daily caps, stable-ID receipts, local-day/high-water rules and payload validation.
- `src/Relationships/RelationshipStore.cs`: independent `<dataDirectory>/relationships/state.json`, reparse checks through `CompanionPackStorage.AssetPath`, exclusive file lease over read/copy/mutate/validate/atomic-save, durable temporary writes and atomic replacement.
- `src/Relationships/RelationshipService.cs`: transactions, detached snapshots, settings, notification claims, failure results.
- `tests/RelationshipRulesTests.cs`, `RelationshipStoreTests.cs`, `RelationshipIdentityTests.cs`, `RelationshipTestSupport.cs`: real files, actual exclusive-file save failures, restarts and eight concurrent service instances.
- `Build.ps1`, both Core/Tests csproj files and `PortableTests.cs`: Framework targets, shared sources, portable tests, default suite and `--relationships-only`. Preserved the Build.ps1 UTF-8 BOM for Windows PowerShell compatibility.

Stage is always derived from score. Persisted milestones retain their original stage ID, earned UTC/local date and claim flag. Category counters are awarded **points**, with maxima 2/6/8/8; their sum equals DailyTotal (maximum 20). Work and conversation with the same logical event ID cannot both earn credit. All valid processed event IDs remain recorded, including capped/cooldown events; disabled-growth and rolled-back events are ignored. No history is reconstructed.

Normal midnight resets the budget even within 24 hours. An offset change carries all counters to the new observed date and cannot create activity by itself. The maximum observed date blocks reopening previously claimed anchors; UTC rollback does not mutate state. Actual normal active dates continue at score 1000; away days are never generated. Inclusive acquaintance span is derived separately and clamped to at least one day after first meeting.

Stores are bounded at 32 MiB, 4096 companions and 100000 receipts/active dates per companion. Reaching a bound returns failure and retains old data; receipt IDs are never evicted. Malformed/newer-schema/unreadable data blocks transactions and is preserved, without fallback defaults overwriting the file. There is no recovery UI in M1.

## Fixed consumer APIs (namespace Dafeiyu)

```csharp
public static class RelationshipIdentity {
    public static string Resolve(string dataDirectory, string pickerKey,
        string verifiedOfficialCharacterId, string legacyStableId);
    public static bool IsValid(string id);
}
public enum RelationshipEventType {
    Visit, UserInteraction, CompletedConversation, SuccessfulWorkTask
}
public sealed class RelationshipTime {
    public RelationshipTime(DateTime utcNow, TimeSpan localOffset);
    public DateTime UtcNow { get; }
    public TimeSpan LocalOffset { get; }
    public string LocalDate { get; }
    public static RelationshipTime Current();
    public RelationshipTime AddSeconds(double seconds);
}
public static class RelationshipRules {
    public static string StageForScore(int score);
}
public sealed class RelationshipStore {
    public RelationshipStore(string dataDirectory);
    public string FilePath { get; }
}
public sealed class RelationshipService {
    public RelationshipService(RelationshipStore store);
    public RelationshipSnapshot GetSnapshot(string companionId);
    public RelationshipResult Record(string companionId,
        RelationshipEventType eventType, string logicalEventId, RelationshipTime time);
    public RelationshipResult UpdateSettings(bool growthEnabled,
        bool feedbackEnabled, bool aiContextEnabled);
    public RelationshipResult ClaimMilestone(string companionId, string stageId);
}
```

`RelationshipTime` requires DateTimeKind.Utc, years 1970–9998 and an integral minute offset in [-840,840]. Production construction: `var now = RelationshipTime.Current();` or `var stamp = DateTimeOffset.Now; new RelationshipTime(stamp.UtcDateTime, stamp.Offset);`. For precision fixtures use `DateTime.AddTicks`; Framework's own `DateTime.AddSeconds(double)` rounds fractional milliseconds.

`RelationshipSnapshot` properties (public getters):

- `RelationshipSettings Settings` (global persisted settings; GrowthEnabled, FeedbackEnabled, AiContextEnabled all bool, default true).
- `CompanionRelationshipState State` (null until first accepted relation event).
- `string StageId` (derived; first_meeting for an unstarted companion).
- `int AcquaintanceDays(RelationshipTime time)` (zero before first meeting).

`CompanionRelationshipState` public get/set properties on the detached snapshot:

- string: `CompanionId`, `FirstLocalDate`, `BudgetLocalDate`, `DateHighWater`.
- int: `Score`, `OffsetMinutes`, `DailyTotal`, `VisitPoints`, `InteractionPoints`, `ConversationPoints`, `WorkPoints`.
- DateTime UTC: `FirstMetUtc`, `LastSeenUtc`; nullable DateTime UTC: `LastRewardedInteractionUtc`.
- `List<string> ActiveLocalDates` (ISO yyyy-MM-dd, sorted unique actual effective days).
- `List<RelationshipReceipt> Receipts`; each has `string EventId`, `RelationshipEventType EventType`, `DateTime Utc`, `int AwardedPoints`.
- `List<RelationshipMilestone> Milestones`; each has `string StageId`, `DateTime EarnedUtc`, `string EarnedLocalDate`, `bool NotificationClaimed`.

Stage IDs and thresholds: first_meeting 0, familiar 80, close 240, trusted 480, heartfelt 760, best_friend 1000. Snapshot edits cannot mutate persisted state; no public save-mutable-snapshot API exists.

`RelationshipResult` public getters: `bool Success`, `int AwardedPoints`, `bool ClaimedNotification`, `string Error`, `RelationshipSnapshot Snapshot`. On a failed save, points/claim are zero/false and Snapshot is the previous committed snapshot. If loading failed, Snapshot is null. Input failures also return Success=false with no scoring. `GetSnapshot` throws invariant/I/O exceptions on invalid input or unreadable/corrupt/newer data; adapters should localize their presentation. `UpdateSettings` returns a settings snapshot with State=null. A duplicate or capped event is a successful result with zero points. Only show a milestone notification when `Success && ClaimedNotification`, after the claim has been committed.

## WPF and Avalonia integration instructions

For WPF `SkinDefinition` and Avalonia `PetSkin`, use the committed companion's canonical `PickerKey`; pass `OfficialCharacterId` only when assigned by the existing validated official catalog/installation path. Existing `OfficialCompanionIdentity.NormalizePickerKey` validates picker provenance. A `custom-pack:*` key stays custom even if metadata supplies an official ID. An `official:*` key requires the matching verified official character ID. Unverified official keys are rejected, never promoted.

For outfits, pass **the parent companion's** identity fields. Do not derive identity from outfit, artwork, avatar, display name, package content claims or prompt/persona names. For pre-picker legacy entries, pass the existing persistent skin ID as legacyStableId with empty pickerKey; SHA-256 of a fixed namespace plus that ID deterministically produces legacy-skin:skin-<32 hex>, so no sidecar assignment is required. Valid existing legacy-skin picker aliases are retained. Reuse the same caller-owned logical event token on retries; use one logical task token for a work execution so it cannot also be awarded as a conversation. Emit only successful, complete events in M2. The service is synchronous and coordinates disk transactions; UI adapters should schedule I/O appropriately.

New visible-resource keys required by this core: **none**. Errors are clear internal invariant exceptions/messages, as agreed with the controller. M2/M6 must localize their user-facing error surfaces.

## RED/GREEN evidence

Logs live under `.superpowers/sdd/companion-lifecycle/logs/` in this worktree (local verification artifacts, not product assets).

1. Wrote identity/rules/store behavior tests and registered Framework/portable targets before product implementation. `./Build.ps1 -Target RelationshipIdentityTests` failed as expected with CS0246 for absent RelationshipStore/RelationshipService/RelationshipTime: `task-1-red-framework.log`. This initial absent-contract RED is a compile failure, not a runtime assertion failure.
2. Runtime RED for backward travel followed by catch-up: `task-1-backward-anchor-red.log` failed `backward-zone date catches up without reopening budget: Invalid relationship payload`. Kept BudgetLocalDate current while carrying budget; final rules runs pass.
3. Runtime RED for malformed persisted first meeting, offset anchor, cooldown timestamp and last seen: `task-1-payload-red.log`, `task-1-offset-payload-red.log`, `task-1-interaction-payload-red.log`, `task-1-last-seen-red.log`. Added consistency checks against real receipt boundaries and awarded interaction receipts; final store runs pass.
4. Runtime RED for submillisecond rollback across restart: `task-1-precision-red.log`. Replaced precision-losing serialized DateTime encoding with UTC ticks; final store tests also verify a 19.9995-second interval stays ineligible.

Final successful commands:

```powershell
# cwd E:/workspace/deskpet/companion-relationship
./Build.ps1 -Target RelationshipIdentityTests
./Build.ps1 -Target RelationshipRulesTests
./Build.ps1 -Target RelationshipStoreTests
# cwd E:/workspace/deskpet, outside desktop/global.json
dotnet run --project E:/workspace/deskpet/companion-relationship/desktop/DeskPet.Tests/DeskPet.Tests.csproj -- --relationships-only
```

Exit 0 for all focused targets. `task-1-green-identity-framework.log`: 7 passed. `task-1-green-rules-framework.log` and `task-1-green-store-framework.log`: all behavior scenarios passed. `task-1-green-portable.log`: all three suites passed using installed SDK 10.0.203 without changing global.json. Framework compiles all product source using its existing C# 5 compiler. Existing unrelated CS0649 and portable CA1416 warnings remain; no new relationship warnings were observed. `git diff --check` passes.

Covered: score/stage boundaries 79/80,239/240,479/480,759/760,999/1000; daily19 partial reward to20; category caps; max-score actual active days; independent companions; spoofing, same-name custom packs and outfits; 20sec cooldown; event IDs across restart/type/day; midnight, UTC rollback, DST forward/backward, same-instant travel, future/backward date/offset changes; persisted settings; failed atomic save keeps snapshot and supports same-ID retry; failed claim supports retry; byte-identical malformed/newer/negative/impossible payloads; size bounds; interleaved/eight concurrent services; one committed notification claim; detached snapshots; inclusive acquaintance span without invented activity.

## Full portable suite and limitations

Attempted full default suite using `dotnet run --no-build --project E:/workspace/deskpet/companion-relationship/desktop/DeskPet.Tests/DeskPet.Tests.csproj` from E:/workspace/deskpet. Log: `task-1-full-portable.log`. The suite used the earlier, pre-hardening build. It stalled on blocked loopback model-catalog fixtures; at the controller's instruction it was stopped via Ctrl-C on its own execution session after more than eight minutes without progress. It did **not** complete or pass. No unrelated test or provider implementation was modified.

Every observed `FAIL` prefix from that run is listed here:

- RelationshipRulesTests: `daily19 fixture actual 15` (early run; setup originally ignored failure results. Setup now explicitly checks Success and reports returned Error; final Framework and portable runs pass. The exact cause of that early missing award was not captured.)
- RelationshipStoreTests: InvalidDataException escaping validation (early build; fixed by explicit InvalidDataException failure handling, final both-runtime runs pass).
- SystemLanguageTests: independent startup follows OS/manual preference; child fixture printed Usage.
- SavedSelectionAppliesInANewProcess: Language startup fixture printed Usage.
- ReplyLanguagePolicy: zh_CN fixture printed Usage.
- ReparseRootsAndMetadataRejected: Directory link fixture failed: 5.
- holiday state: UnauthorizedAccessException, Access to the path is denied (sandbox redirected TEMP atomic rename).
- holiday assets: Run asset verification from the repository or a child directory (suite was intentionally launched outside the SDK-pinned worktree).
- attachments and reasoning use separate protocol fields in JSON and streaming fallback: service connection failed.
- model listing uses GET without requiring a selected model and preserves endpoint prefixes: service connection failed.
- Anthropic model catalog uses its own authentication and version headers: service connection failed.
- model catalog errors do not leak server response details: unsafe model catalog error.
- incompatible model catalog and invalid pagination are rejected safely: unsafe malformed catalog error.
- model catalog fetch is cancellable while waiting for the response: service connection failed (last output before stall).

An initial attempted portable rebuild while that earlier full suite still held DeskPet.Core.dll produced MSB3027/MSB3021. After stopping only that owned full-suite session, the final portable build/run completed with exit 0. Relationship fixture directories use isolated workspace `work/deskpet-relationships-*` paths because a real File.Move probe demonstrated that sandbox redirected TEMP forbids atomic rename; fixture cleanup verifies the absolute workspace parent before recursion.

Remaining verification is M6's full-suite rerun under permissions suitable for local directory-link and loopback fixtures, with SDK-compatible invocation preserving repository resource lookup. This is not a claim of a pristine whole-project suite or end-to-end UI acceptance. Receipt-store bounds intentionally fail closed; a future retention design must preserve durable dedup guarantees rather than discard receipt IDs. No other known implementation blocker remains after focused verification and self-review across correctness, readability, architecture, security and performance.
