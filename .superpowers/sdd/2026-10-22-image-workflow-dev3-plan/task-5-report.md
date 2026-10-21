# Task 5 implementation report

Base: d695468dcfd266d2938c3c88b16052e2ee3c84d4
Implementation commit: a33cb17a22cde371b0aca8f96b93178cec4b933a
Branch: codex/image-workflow-v0.10.2-dev.3

## APIs and handoff

- Shared C#5 `GeneratedImageGroupState.Update(taskIds, assets, find)`, `Keys`, `SelectedId`, `SelectedImage`, `ImageFor(id)`, `Select(id)`. One instance per message; ordered immutable task/asset keys; first slot remains selected until explicit selection. Updates preserve selected ID while results/IDs append.
- Native WPF/Avalonia `GeneratedImageGroupCard(owner, host)` + `Update(ChatMessage)` and internal `SelectedId`. Avalonia implements IDisposable. Existing ChatMessageView owns one group across text streaming and appended results.
- WPF and Core explicitly compile shared state (Core csproj added link). Standalone `GeneratedImageGroupTests` Build target; portable `--image-group-only` switch and explicit test source link.
- Resolve assets from message metadata, FindByJobId, then retained snapshot ResultImageId. Missing task and asset show 图片记录不可用, rather than an endless pending card. Existing legacy asset order remains visible. Task center retries do not mutate message IDs.
- 480 DIP breakpoint; right vertical 64 DIP thumbnail buttons on wide layouts; bottom horizontal scroll on narrow layouts; single slot hides rail. Selected thumbnail border and native theme surface. Existing GeneratedImageCard owns preview/save/copy/details for selected asset; WPF repeat preserves existing chat/workspace routing.
- Manager subscriptions exist only loaded/attached; selected pending card reuses Task4 lifecycle. Avalonia releases selected card and decoded thumbnail Bitmaps on replacement/detach/dispose; WPF decoded frozen BitmapSources have no disposable ownership. No timer or HTTP client introduced. Frame draws do not rebuild chat/group.
- Parked Task4 P3 fixed: WPF completion fade now requires per-control MotionEnabled as well as preference and OS animation. Native disabled completion assertion included.

## Commands and observed results

All commands executed from E:\workspace\deskpet\image-agent-tools. Test handlers are in-memory fixtures; no live/paid service calls or CI operations.

1. RED: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target GeneratedImageGroupTests`
   - Before implementation: CS0246 GeneratedImageGroupState missing; compilation failed. Full output task-5-red.log.
2. GREEN same command: 7 passed, named assertions:
   - OutOfOrderCompletionKeepsSlots
   - UserSelectionSurvivesLaterResults
   - MixedSuccessFailureAndPendingCanSwitch
   - ActionsTargetSelectedImage
   - SeparateMessagesNeverMerge
   - HistoryRestoresGroupOrder
   - RetentionDroppedSnapshotResolvesByJobId
   - Full final output task-5-green.log.
3. `dotnet build desktop\DeskPet.Tests --no-restore -m:1 -v:quiet`
4. `dotnet run --project desktop\DeskPet.Tests --no-build -- --image-group-only`
   - 7 passed on portable runtime; task-5-portable-build.log and task-5-portable.log.
5. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Target ImageGenerationUiTests -TestArguments assets\pet-preview-hd.png`
   - Final: Image generation UI tests: 71 passed; task-5-wpf.log.
   - Includes native group identity and selected ID across text/append, selected action asset metadata, retained asset lookup with absent task snapshots, real fixture running/canceled slots, explicit unavailable slot, twenty slots, four responsive theme cases, disabled completion fade, plus pre-existing preview/copy/workspace checks.
6. `dotnet build desktop\DeskPet.Desktop.Tests --no-restore -m:1 -v:quiet`
7. `dotnet run --project desktop\DeskPet.Desktop.Tests --no-build -- --image-ui-only`
   - Final build 0 errors; PASS: 133 image workspace / gallery / conversation UI assertions; task-5-avalonia-build.log and task-5-avalonia.log.
   - Same new native identity, selection, retention, running/canceled/unavailable switching, 20 slots and four theme/breakpoint cases; existing clipboard/preview checks remain green.
8. `git diff --check`: clean (only CRLF normalization warnings). Build.ps1 first bytes verified EF-BB-BF. All WPF targets invoked with Windows PowerShell5.1.

Native fixture iterations initially exposed only test setup problems: inaccessible ChatMessage private setters (use native constructor/internal helper or reflection in portable tests), extra fixture assets affecting old gallery count/order assumptions, and WPF click visual-tree assertion needing dispatcher layout time. These were corrected; the final full focused suites pass.

The default parallel dotnet invocation exited1 with zero compiler errors on this environment; serial -m:1 is the established working invocation. Existing cached NU1900 and nullable/obsolete API warnings remain. No restore/network cleanup attempted.

## Screenshots

Reference inspected before implementation: docs/plans/assets/image-workflow-dev3/conversation-image-group.png.

Final meaningful image fixture: existing assets/pet-preview-hd.png, rendered through native controls.

WPF durable evidence:
- E:\workspace\deskpet\image-agent-tools\work\image-ui-evidence\group-light-760.png
- E:\workspace\deskpet\image-agent-tools\work\image-ui-evidence\group-light-390.png
- E:\workspace\deskpet\image-agent-tools\work\image-ui-evidence\group-dark-760.png
- E:\workspace\deskpet\image-agent-tools\work\image-ui-evidence\group-dark-390.png
- E:\workspace\deskpet\image-agent-tools\work\image-ui-evidence\group-20-slots.png

Avalonia durable evidence:
- E:\workspace\deskpet\image-agent-tools\work\task5-avalonia-evidence\group-light-760.png
- E:\workspace\deskpet\image-agent-tools\work\task5-avalonia-evidence\group-light-390.png
- E:\workspace\deskpet\image-agent-tools\work\task5-avalonia-evidence\group-dark-760.png
- E:\workspace\deskpet\image-agent-tools\work\task5-avalonia-evidence\group-dark-390.png

Root visually inspected refreshed WPF and Avalonia dark wide / light narrow evidence and accepted native rail and theme presentation. Initial 1px test screenshots were replaced; final Avalonia screenshots are outside bin.

## Self-review and limits

Scoped self-review used code-review-and-quality across correctness, readability, architecture, security and performance. Removed obsolete ChatMessageView card list; preserved repeat routing; expanded thumbnail decode error handling. No dependency, version, release, tag, network path, task manager or paid request added.

- Existing WPF GeneratedImageCard 图片详情 Expander header is black in dark theme; root requested noting this for Task9 rather than expanding Task5.
- Native new mixed-state fixture exercises running and canceled terminal states; failed terminal rendering reuses the existing pending-card label/lifecycle. The shared test covers unavailable/pending vs success selection without coupling selection to task state.
- Selected action targeting is verified through the selected card asset metadata plus existing native preview and clipboard checks; save dialog/file explorer interaction is not automated.
- Three asset slots plus mixed task slots and 20 total slots are exercised. Existing gallery tests supply mixed image ratios; the new group screenshots use the same legitimate image at each completed slot.
- Report/logs are confined to this task's authorized report/log artifacts. No other plan/ledger files changed. No subagents or reviewers spawned.
