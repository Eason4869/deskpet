# Changelog

[简体中文](CHANGELOG.md) | [English](CHANGELOG.en.md)

## 0.11.2-dev.1 — 2026-10-27 · Source preview (no Release)

Based on main 0.11.1, submitted to dev; stable main remains unchanged.

- Native outbound messaging for QQ, WeChat, Feishu, DingTalk and WeCom, with credentials/QR login, allowlists and lifecycle controls under Settings → Messaging channels. No public IP, developer server or additional bot runtime.
- Isolated identities, bounded context/queues, encrypted profiles and no remote Agent tools. All 52 messaging strings are translated in all 13 locales.
- Fix cancellation races, stop at dedup capacity, WeChat poll timeouts/extra verification, Feishu auth/heartbeat, DingTalk registration readiness and cancelled QR saves. Include an offline guide and Windows/macOS CI checks.
- See the [plan audit](docs/im-channels-plan-audit.md) and [verification record](docs/im-channels-acceptance.md). Real-account and real Mac login acceptance remain pending; this source tag does not provide installable Release assets.

## [0.11.1](https://github.com/Eason4869/deskpet/releases/tag/v0.11.1) — 2026-10-27 · Stable

Published from main, combining the 0.10.5, 0.11.0 and 0.11.1 development work since stable 0.10.4. Assets include Windows installer/portable/standalone application, both macOS preview architectures, source, five complete companion packs, pack index, authoring template and unified SHA256 checksums. Mac packages remain ad-hoc signed, without Developer ID signing or notarization.

- Support thirteen languages and System default. WPF and Avalonia apply saved language changes immediately while retaining drafts, windows, unsaved settings and running tasks. Existing requests keep their starting language; new requests use the new choice. Custom personalities and existing content remain intact.
- Add companion home, separate relationship records, stage welcomes and interaction feedback. Score saved encounter, interaction, successful-conversation and root-work-task facts with a daily cap of 20; old chats do not manufacture gains. Growth, feedback and AI stage context can be disabled.
- Provide 55 appearances across five complete official packs: default plus ten holiday outfits per companion, preserving HD artwork, complete actions and stable identity. Local-date wardrobe changes respect multi-day National Day, Spring Festival and Christmas periods; other events last one day. Normal selection returns afterward, with independent role/source/holiday unlocks.
- Support ten holiday keepsakes, adding original scenes and decoration for nine holidays across all five roles. Eligible real visits save the current year's card; previews and past dates do not grant ownership. Exports retain original scenes, historical names, dates and digests.
- Persist encrypted new-conversation drafts by companion, mode, project and section across restarting. Record shortcut keys with clear, conflict detection and save, preserving cancellation and focus-loss behavior.
- Support application-owned background jobs in persistent Windows WPF and Avalonia work conversations: original-reply cards, project lists, paged logs, stop and retry. Preserve process-tree ownership; local previews open only after an explicit click. macOS provides conversations and task-record UI without local background-process execution.
- Release application 0.11.1 while retaining actual material version 0.11.0-dev.3 in the five official ZIPs. Unchanged materials retain identical bytes/digests and verified installed content is reused across releases without receipt rewriting. Conflicting metadata preserves existing material and reports an error. Full packs allow 512 MiB and receipts use the shared 1024-file limit.
- Add transactional current/recovery publication and interrupted-state recovery. Managed history becomes eligible after seven retirement days for manual or successful-install background cleanup. Protect current, recovery, active, unknown, legacy and custom resources. Verify each managed offline query once instead of twice, without permanent trust caching; maintenance failures never change committed install success.
- Preserve drafts, media, attachments, relationships and project data across the documented maintenance operations. Uninstall requires in-app confirmation; cancellation, Escape, closing and preparation failure keep the companion usable. Correct stumble, download and list-removal copy, align release/runtime documentation, and repair language snapshots, standalone updater dependencies, Unix lock waits and packaging/native-preview acceptance.

Resources, placeholders, provenance and packaged output are checked; generated translations have not received native-speaker review. See the [setup guide](docs/configuration-guide.md), [relationship guide](docs/companion-relationship/user-guide.en.md), [character packs](docs/character-packs.md) and [release naming](docs/release-naming.md). Historical development records retain their original status below.

## 0.11.1-dev.3 — Source tag (no Release)

- Schedule protected managed-history cleanup after successful installation, with the same seven-day grace, integrity and reader safeguards as manual maintenance. Update/download caches are excluded.
- Support cancellation and bounded shutdown. Maintenance/reporting failures never change committed installation success; unverifiable legacy or partial leftovers remain protected.
- Verify managed offline queries once instead of twice, retaining full per-file verification under the lifecycle lock and a read lease. Later tampering is still rejected.

## 0.11.1-dev.2 — Source tag (no Release)

- Refresh intentional catalog byte snapshots and check them early in Windows/macOS CI while preserving independent historical translation evidence.
- Keep the standalone updater independent of companion maintenance and recognize Unix file-lock contention during bounded retries.
- Validate material asset versions independently and compare guide numbering and titles with the compiled application. Retain pinned source-export builds alongside the strict CI history gate.
- Keep idle connections from blocking the native preview test server; preserve real-browser checks and the original navigation timeout, with retained failure diagnostics.
- Add transactional current/recovery-version publication and interrupted-state recovery. Maintenance errors do not hide verified installed resources.
- Add managed character history cleanup after seven retirement days. Protect current, recovery, active, unknown and legacy roots; legacy lazy readers cannot select managed roots.
- Clarify cache preservation and list removal across both UIs and all 13 languages, and update current usage guidance.

## 0.11.1-dev.1 — Source tag (no Release)

- Separate per-character material versions from application releases, with payload digests and independent compatibility requirements. Unchanged materials retain identical ZIP bytes.
- Reuse verified installed content across releases, reject conflicting metadata, and apply the shared 1024-file receipt limit.
- Correct stumble capability and setup download copy across all 13 languages while retaining historical translation evidence.

## 0.11.0-dev.3 — In development (unreleased)

- Integrate five complete companion packages, each with the default outfit and ten holiday outfits. Preserve original artwork, actual action poses, blinking, petting and body-poke reactions.
- Recognize holidays using system local time, automatically restore the selected outfit afterwards, and keep permanent unlocks separate for each holiday and companion source.
- Add 45 distinct keepsake illustrations for the nine additional holidays, with native localized text and export.
- Persist unsent new-chat drafts across window closure and restart, isolating roles, modes, projects and temporary quick-chat inputs.
- Record shortcut combinations from actual key presses, with conflict checks and temporary suspension of global hotkeys.
- Support complete packages up to 512 MiB with 1024 entries, atomic installation, cancellation, disk-space preflight and bounded image ownership. Candidate resources require a compatible client and a separate verified resource publication.

## 0.11.0-dev.2 — In development (unreleased)

- Apply language changes to existing WPF and Avalonia controls while preserving drafts, unsaved settings, window state, selections and running work.
- Replace the guide language restart action with Apply and continue; retain recovery and update restarts that are still needed.
- Save only language preferences. Existing configuration read/write failures preserve the prior configuration and effective language.
- Keep an in-flight reply, Agent subrequests and Live connection in their original language; new requests use the new language. Live system notices refresh while history and custom text remain unchanged.
- Validate through a staged draft PR. See the [acceptance record](docs/localization/live-language-acceptance.md) for evidence and platform limitations.

## 0.11.0-dev.1 — Source preview (no binary Release published)

Based on the explicitly selected v0.10.5-dev.3. See the [local acceptance snapshot](docs/companion-relationship/acceptance.md) for Windows candidates and complete regressions; macOS hardware remains unverified. A source tag does not announce a downloadable binary Release.

- Fixes keepsake album sizing after a constrained window grows from a narrow layout. Cards follow the arranged content width and retain two readable columns when space allows.
- Allows active text, voice, regeneration and speech operations to finalize after the host UI dispatcher stops, preserving bounded exit and deferred service cleanup.
- Ordinary affection notices now rise and fade in, pause briefly, then rise and fade out. Consecutive gains merge; interaction responses and milestones retain longer reading time. Notices avoid the companion at screen edges, after content grows and as the companion moves, keeping its input surface clear.
- Longer relationship stage names, including Japanese labels, adapt to six, three, two or one column without splitting a stage name.

- Adds companion home within the existing main window, a per-identity relationship ledger, stage welcomes and actual user-interaction responses while preserving persona priority, drafts and navigation. Encounters, interactions, completed conversations and successful root work tasks settle saved facts, capped at 20 points daily; old chats/background recovery do not reconstruct rewards.
- The left profile card shows a complete raw scene with name and biography. Official companions start with introductory first-meeting artwork, then use their highest earned stage; the stage welcome stays in the right relationship card. Introductory art creates no score or keepsake record.
- Relationship controls pause growth or disable feedback/AI stage context. Passive combined feedback does not steal focus or replay stale notices. Original artwork, a readable right-side gradient, stage/holiday ornaments and full 800×480 keepsake PNGs retain real dates and explicit cancellation/failure behavior.
- Holiday albums distinguish locked previews from owned cards. Only Halloween from 2026 is currently collectible through a real eligible local-date encounter and trusted identity, deduplicated annually with the original scene retained. No extra affection/outfit grant, cross-date retry or retrospective collection.
- Cache cleaning protects active/uncertain resources. History clearing retains drafts, images, attachments, relationships and project facts, with retained attachments available after restart. Preference reset preserves language, model credentials, personas, update channel and onboarding. Uninstall confirms within the app; cancellation, Escape, close and preparation failure keep the companion running.
- Updates all five complete companion packs to 0.11.0-dev.1 while retaining legacy atlases, stable identity and saved historical cards. All 30 quiet clips have eight frames: idle at 4FPS / 2 seconds and both sleeping variants at 2.5FPS / 3.2 seconds, with original pixels checked against final packages.
- Completes new feature copy in 13 locales, literal resource keys, independent text/format and committed-source provenance, and regenerated title subsets with native Hindi shaping support. All 4507 original values per locale are retained; assistant-authored translations have not received native-human proofreading.
- Adds settings feature/subpage discovery, recovery feedback, newer-schema protection and bounded default/CI relationship/keepsake coverage. Source display version is 0.11.0-dev.1 and assembly version 0.11.0.0; existing stable/preview channel and stable latest-link semantics are preserved.

[User guide](docs/companion-relationship/user-guide.en.md) · [中文](docs/companion-relationship/user-guide.md) · [Schemas and stable identity](docs/companion-relationship/schema-and-packs.md).

## [0.10.5-dev.3](https://github.com/Eason4869/deskpet/releases/tag/v0.10.5-dev.3) — 2026-10-24 · Preview

This preview includes the dev.1 UI resource refactor and dev.2 multilingual support, Agent background jobs and background companion preparation, with the setup improvements below. Stable latest remains 0.10.4; preview updates never downgrade the app.

- Before you start offers a blue Start guide action and a gray Explore on my own action. Exploration skips formal setup and goes directly to congratulations; both paths require explicit final confirmation to mark the guide complete.
- Explain stable (main) and preview (dev) channels after update-network setup, including stability, update eligibility and no-downgrade behavior. Windows uses the existing persisted channel selector; the manual-update frontend provides a releases-page entry.
- Add a language homepage with all thirteen languages and System default. The Restart caption immediately follows the selected language; the app saves the choice, shuts down normally, restarts and resumes at Before you start. Keeping the active language allows direct continuation.
- Add an estimated 15–25 minutes for the full guide, with extra time possible for character downloads and model service setup.
- Center the guide when no main window is visible and anchor it to the right of an open main window. Changing steps no longer moves the guide around highlighted controls. Keep the guide within the working screen on both WPF and Avalonia, preserving existing progress and completion policy.

## 0.10.5-dev.2 — Development snapshot (no packages)

- Added a language-selection dialog above Settings in the avatar menu, with 13 languages and Follow system. The default follows the system UI language while preserving saved manual choices; unsupported languages use English. Saved changes take effect at the next startup. The dialog follows the theme and scrollbar styling, with two columns at normal widths and one in narrow windows.
- Integrated and completed the user-provided Traditional Chinese, English, Japanese, and Korean packs; added French, German, Hindi, Indonesian, Italian, Brazilian Portuguese, Latin American Spanish, and Spanish (Spain). Completed the language menu, companion descriptions, official character displays, voice descriptions, and shortcuts page. Translation sources and subsequent edits are recorded separately; automated structural validation does not constitute native-speaker review.
- Localized default personalities for the effective language and requested replies in that language, preserving custom personalities, history, and stable character identities. Windows system speech prefers an available voice matching the language, retaining the system default if none matches.
- Unfavorited gallery images now use an outlined heart with the same shape and size as the red favorited heart; corrected search-caret and placeholder alignment.
- Added background-task contracts, separate time limits, persistent records, bounded logs, a task manager, native Windows Job/AppContainer process ownership, and the `start_job` / `get_job` / `stop_job` tools. Validated cross-turn session ownership, idempotency, authorization boundaries, and cleanup, preserving existing synchronous-command timeout rules. WPF now includes application services, project-operation guards, exit cleanup, original-reply task cards, a task list and plain-text logs; local previews open only after a click. Avalonia also supports persistent work conversations, shared windows, source isolation, task UI and native Windows exit cleanup; macOS provides task records without local execution. Added terminal consent and separate time-limit controls, and fixed final-log failure or canceled reads disrupting task finalization.
- First-run setup places GitHub routing before character selection and an explicit full official-pack download/update recommendation. Preparation belongs to the application, so later guide steps, closure and resume remain available. Installed roles stay usable; completion reloads the last selected role, while failure or cancellation allows retry. Updated the bundled 132-step configuration guide and character-selection instructions.
- Changed standalone updater validation to check embedded language resources and non-language size for each package, retaining checks against accidentally including character assets and accommodating the full multilingual catalog.

## 0.10.5-dev.1 — Development tag (no packages)

- WPF, Avalonia, macOS platform prompts, and shared business text now consistently read embedded `zh_CN.json`, preserving original Chinese wording, regional formatting, and historical data.
- Separated availability, defaults, speech results, menu actions, browser error precedence, and character-package routing from display text.
- Added an independent text baseline, item-by-item migration audits, missing-resource diagnostics, and resource validation for both runtimes and built artifacts, integrated into Windows and macOS CI.
- Includes the character-restoration, silent startup-update, and safe hot-reload fixes from 0.10.4; this iteration adds no Agent background tasks.

## [0.10.4](https://github.com/Eason4869/deskpet/releases/tag/v0.10.4) — 2026-10-23

Emergency fix for 0.10.3: when reopening the app after exit, restore the previously selected installed companion first, instead of switching to Ebao and waiting for an online restore.

- Windows WPF and Avalonia share existing character-identity and cache-validation rules. Valid local character packages restore offline, preserving saved personalities and the character's holiday assets.
- Missing, damaged, or incompatible character packages still fall back safely to Ebao while retaining the character pending restoration. Compatibility rules for explicit switching, hidden characters, custom characters, and legacy data remain unchanged.
- Switch to an available older character package first, update it in the background, and automatically reload the same character when safe. The main avatar and Settings retain progress dialogs during actual downloads/updates; first downloads from the pet menu use a waiting bubble. Version checks and cache reuse show no prompt. The current companion stays in place during the first download. Character options are not disabled merely because a package has not been downloaded or updated.
- After restoring the current character at each startup, silently check and update its package in the background, including built-in Ebao without a standalone package installed. Keep old assets usable and hot-reload safely once updated, without requiring a manual character switch.
- This version contains only character-restoration and switching fixes with regression validation. The language-JSON refactor continues on the separate 0.10.5-dev.1 development branch; background tasks are not included.

## [0.10.3](https://github.com/Eason4869/deskpet/releases/tag/v0.10.3) — 2026-10-23

Stable release merging the latest dev into main, combining the 0.10.1 and 0.10.2 development series and 0.10.3-dev.1. Provides Windows installer/portable packages, full source, complete HD character packages for all five companions, a character catalog, and authoring templates. macOS Apple Silicon/Intel builds remain ad-hoc previews without Developer ID signing or notarization.

- Added complete Halloween outfits for all five official companions, cloud-and-mist outfit changes, permanent collection of hover Easter eggs, a wardrobe, and holiday greetings. The app includes complete Ebao assets; other companions download on demand, with options to download/update everything while retaining usable older assets and personal records.
- Added an image-generation workspace, six service presets, reference images, and a shared generation queue. Supports a task center, cancellation, explicit retries and recovery, switching between multiple images in chat, gallery favorites/filters/multiselection/original-image export, and a recycle bin protected by reference tracking.
- Added ask-user, local-image inspection, and read-only Git tools to work mode; read-only Git rejects executable filters. Supports discovery of public GitHub skill repositories, installation verified against pinned commits, ZIP skill import, and HTTP model gateways on local networks.
- Unified Settings and the dark interface; improved the usage matrix, shortcut editing, archive management, project-facts tab, and in-chat operation confirmations. Chat links support right-click actions and opening in side tabs; switching conversations preserves drafts.
- Fixed access to disposed objects in archived conversations, Agent failures being requeued, quick-chat draft restoration, image-result synchronization and saving on exit, gallery-query waiting races, and PowerShell startup and UTF-8 build encoding in managed Windows environments.
- Retained dev's removal of obsolete planning, handoff, and acceptance documents, updating necessary user tutorials. Runtime and source packages do not restore the deleted old docs.

See [Image generation](docs/image-generation.md), [Holiday outfits](docs/holiday-outfits.md), and [Character packages](docs/character-packs.md) for usage and limitations. Historical development entries follow.

## 0.10.3-dev.1 — 2026-10-23

Development candidate based on remote dev. Staged CI checkpoints and local acceptance are recorded separately; no installer release was published.

- Added complete HD Halloween outfits, cloud-and-mist outfit changes, hover Easter eggs, and permanent collection for all five official companions. After the first successful unlock, the Wardrobe entry remains in Settings alongside companion selection at equal width, with thumbnails and outfits saved per companion.
- The app bundles only complete Ebao assets; other official companions download on selection, reusing the app updater's GitHub acceleration and proxy settings. Supports restoring the current companion after upgrades, downloading/updating everything, cancellation, and retries after failure, preserving usable older assets and personal data. Initial onboarding only explains download requirements.
- Redesigned the image-generation page with parameters on the left, a large preview on the right, and a rounded floating input at the bottom. The gallery uses a compact filter toolbar, icon-based selection/favorites, and a default masonry layout. Unified dropdown arrows and fixed text clipping.
- Shared offline holiday dates, unlock storage, and cooldown rules now serve both Windows and Avalonia. Changing outfits preserves companion identity, chats, voices, and work tasks.
- Proactive greetings retain their existing frequency, adding six short Halloween lines per character and actual-date context. Local fallback is used when AI is disabled.
- Fixed runtime errors caused by cached entry points accessing a disposed conversation after the current conversation was archived, and the second exception when subsequently closing the window.
- Added checks for complete actions, dual animation sets, gaze, transparent edges, and resource packaging; fixed a gallery regression-test waiting race when switching between queries with the same image count.

See [Holiday outfits](docs/holiday-outfits.md) for instructions.

## 0.10.2-dev.3 — 2026-10-22

This entry records a local development candidate; publication status is determined by the corresponding Release. See [Image-generation guide](docs/image-generation.md) for features and platform limitations.

- The page, configuration tests, and chat now share an encrypted task journal and generation queue, with stages, cancellation, explicit retries, result recovery, and chat-synchronization fixes. Restarting does not automatically resend requests.
- Added a task center, concurrency and animation preferences, completion notifications, and rounded dot-matrix animations that stop redrawing when hidden.
- Multiple images in one reply switch through stable slots, adapt to narrow windows, preserve selection, and direct preview/copy/save actions to the current image.
- Added gallery favorites, filters, sorting, multiselection, batch original-image export, a recycle bin, and restoration. Permanent cleanup protects history and shared reference images through reference leases and recoverable transactions.
- Fixed failure to reopen the original draft when expanding an unsaved quick chat, retaining original-session-directory validation for image repair.
- Fixed lost focus and redundant decoding caused by gallery selection rebuilding thumbnails, and the title color in dark image details. Automated coverage for 40 images at 2048 pixels on both clients is now included in regressions.
- Expanded Windows, full shared-core, and Avalonia default regression registration. Three native-exit paths run in separate processes. Windows CI now compiles the actual installer before tag creation.

## [0.10.2-dev.2](https://github.com/Eason4869/deskpet/tree/v0.10.2-dev.2) — 2026-10-22

Development snapshot based on 0.10.2-dev.1, synchronized to dev with a source tag; no GitHub Release is published.

- Added separate image-generation settings with OpenAI Images, Gemini, Doubao, MiniMax, Qianwen, and Grok presets, supporting custom gateways, models, default parameters, and an independent proxy.
- Added an image workspace in the left navigation for text-to-image, reference-image upload, and cancellation. The gallery supports masonry and equal-size thumbnail grids, large previews and details, Save As, and copy.
- Added Agent `generate_image`, displaying image cards directly in chat. Originals are saved separately; history stores only asset references, supporting restoration after restart and temporary-data cleanup.
- Added built-in `ask_user`, `inspect_image`, `git_status`, `git_diff`, and `git_log` tools, integrated into existing session, cancellation, and permission flows.
- Image-model configuration follows the chat-model visual style, implemented on Windows and Avalonia. See the [Usage guide](docs/image-generation.md).
- Merged remote dev fixes for Windows PowerShell UTF-8 encoding, managed-environment startup, and quick-chat regressions.

## [0.10.2-dev.1](https://github.com/Eason4869/deskpet/tree/v0.10.2-dev.1) — 2026-10-22

Development snapshot based on 0.10.1-dev.2; only the dev branch and source tag are pushed, with no GitHub Release.

- Supports HTTP API addresses for self-hosted local-network gateways, retaining ports and API prefixes. Intranet services may omit keys. Text, model lists, streaming replies, ASR, and TTS share validation, with configuration hints updated accordingly.
- Added public GitHub repository discovery under Windows Plugins → Skills: manage repositories and branches, select sources, search, preview read-only content, install after pinned-commit and file verification, save provenance, and integrate with actual load_skill execution.
- Added ZIP skill scanning, preview, and installation, retaining local-directory import. Supports offline cached previews, cancellation, rate limiting, and failure messages. Existing names are not overwritten; removing a repository does not delete installed copies.
- The main Skills page retains its layout, adding only an Add skill dropdown at the top right. Repository discovery, ZIP import, and local import use rounded overlay dialogs within the main window, supporting light/dark themes and narrow windows.
- Quick chat is registered in the conversation list only after its first message is successfully queued; opening it or typing alone no longer creates an empty New conversation.
- Unsent input is cached per conversation during the app session. Switching A/B or expanding quick/full views restores the corresponding draft. Returning to a new conversation reuses the unsent draft in the same scope; the next draft begins after sending.

## [0.10.1-dev.2](https://github.com/Eason4869/deskpet/releases/tag/v0.10.1-dev.2) — 2026-10-21

Development prerelease built from a pinned dev tag. Provides Windows installer/portable packages, macOS Apple Silicon/Intel previews, full source, five character packages, and authoring templates; does not replace stable latest. Mac previews are ad-hoc signed, without Developer ID signing or notarization.

- Fixed disappearing dark usage-matrix background cells and low-activity colors. Uses an independent gray-blue scale, small rounded corners, evenly spaced cells, and months below, distinguishing uncollected, unknown, and actual zero usage, with immediate theme switching.
- Completed dark styling for model cards, provider lists, and key fields; improved cards, input areas, selected tabs, off-state switches, and border hierarchy in General, About, Work settings, and Plugins.
- Unified headings, descriptions, full-width tab bars, and light/dark selection states across Companion, Model configuration, Work settings, and General. Switching tabs preserves unsaved drafts.
- Split General into Appearance and interaction / Shortcuts / Chat history. Completed 33 editable shortcuts with save, disable, restore defaults, and conflict detection; menus and shortcut help update together.
- Hid archived content from the sidebar and centralized it under Work settings → Archived conversations. Supports archived projects and restoration/deletion of each companion's project, project-free, and quick chats.
- Anchored chat background artwork by its visible silhouette at the bottom right of the chat area, removing offsets caused by transparent asset margins. Approval cards, queues, and scrolling messages no longer push the artwork upward.
- Message links support right-click opening in a side tab or default browser and copying the complete link. Side web pages retain the original conversation and draft, adapting tabs to narrow windows.
- Agent confirmations now appear as inline cards above the corresponding conversation input instead of separate approval windows. Improved command previews, expandable details, and light/dark styling, retaining one-time authorization, rejection, and cancellation.
- Retained manually maintained Project facts in a main-window management tab, adding an empty state, selection/editing, save feedback, deletion states, and dark-theme support.
- Web-read failures return tool errors for the Agent to handle. Started Agent tasks no longer return to the input queue after failure; later queued messages pause to avoid duplicate submission. Messages that have not started remain retryable.
- Removed exported assets under `exports/E包头像与立绘` from the source.

## [0.10.1-dev.1](https://github.com/Eason4869/deskpet/tree/v0.10.1-dev.1) — 2026-10-21

Development snapshot; only the dev branch and source tag are pushed, with no GitHub Release.

- Fixed context previews failing to close on a blank-area click; completed cleanup for Esc, window hiding, loss of focus, and switching conversations. Chat-input carets are now white in dark mode and update immediately on theme changes.
- Added Light, Dark, and Follow system themes, selected through thumbnail cards in Settings → General → Visual style, applied and saved immediately. Dark mode uses deep navy tones to distinguish the top bar, navigation, sidebar, and content area; supported in Windows and macOS preview interfaces.
- Increased the onboarding exit button's font size, weight, and contrast, retaining its exit and progress-saving behavior.

## [0.10.0](https://github.com/Eason4869/deskpet/releases/tag/v0.10.0) — 2026-10-21

Stable Windows release. Existing clients can upgrade directly to obtain the new update channels and helper. Provides Windows installer/portable packages, full source, five character packages, and separate authoring templates. macOS Apple Silicon/Intel builds remain ad-hoc previews without Developer ID signing or notarization.

- Windows About now offers Stable (main) and Preview (dev) channels. Stable accepts only stable releases; Preview accepts the highest version from either. Switching channels only saves a preference; updates always forbid downgrades.
- Unified full-version comparison: `0.9.4-dev.2 < 0.9.4-dev.10 < 0.9.4`. The app, updater, and installer validate versions and build commits; the new installer also blocks manual downgrades.
- Update now and restart skips the remaining countdown. Manual checks and automatic updates retain a five-second wait and continue waiting for tasks, drafts, and data to be saved safely.
- Slimmed Windows installer and portable ZIP contents to the app, updater, and necessary user tutorials. Authoring templates download separately; development documents and promotional images stay in source. Upgrades remove only retired files matching old release hashes, preserving user edits.
- Removed retired command-recipe execution, configuration passthrough, and tool schemas, retaining terminal execution, output reading, cancellation, and legacy-configuration read compatibility. Removed duplicate build-source fallback logic.
- Live waveforms fluctuate continuously during steady speech, calculating volume per actual playback chunk and falling back during silence, retaining layout and colors.
- Fixed cross-thread WPF access in update-progress notifications and greeting previews in unified Settings being blocked by the main window. Failed installations retain diagnostic logs and restore the original app and installation records.
- Initial onboarding recognizes dev versions: upgrades across series show guidance normally, while preview revisions and transitions to stable within a series do not repeat it. Added Windows, shared-core, and Avalonia regressions.
- Bundled standalone Guga character packages use full release-version filenames, including dev sequence numbers, preserving the original asset version and content.
- Added a build-only-by-default Windows release workflow. Explicit publication requires validation of immutable tags, channels, complete attachments, and remote hashes. Stable releases build from pinned main tags; the original preview source tag `v0.10.0-dev.1` is retained.

## [0.9.3](https://github.com/Eason4869/deskpet/releases/tag/v0.9.3) — 2026-10-20

- Added the fifth companion, Guga (`gugugaga`), with separate artwork, avatar, regular-chat personality, and voice presets pending listening tests: 240 poses across 17 standard actions, 16 gaze directions, and nine dual-set action categories.
- Complete v2 character packages support direct ZIP import. Stable IDs update assets, avatars, extended actions, and recommended voices while preserving personal personalities, voices, and chats. Bundled and installed versions with the same ID appear once; legacy imports matching original assets merge while preserving personal identity.
- Added a delete button at the right of each Choose companion row. Deleting the current companion switches automatically, retains chats and configuration, and allows restoration; at least one companion must remain. Names remain original without update suffixes.
- Synchronized character packages, dual animation sets, and companion management across WPF and Avalonia. Fixed animation caches and recommended-voice refresh after same-ID updates; consolidated path validation, reused atomic writes, and reduced repeated UI refreshes.
- The complete Guga asset package is named `gugugaga-HD-Skin-0.9.3.zip`. Provides Windows installer/portable packages, full source and authoring templates, plus macOS Apple Silicon/Intel previews. Mac previews are ad-hoc signed, without Developer ID signing or notarization.

## [0.9.2](https://github.com/Eason4869/deskpet/releases/tag/v0.9.2) — 2026-10-19

Improves Windows sidebar interaction, chat sections, and conversation ownership, and fixes usage-matrix spacing. Provides Windows installer/portable packages and dual-architecture macOS previews. Mac retains a separate interface; the Windows interaction changes below do not imply Mac implementation.

- Chat/Work entries show a gray hover background, retaining rounded corners and selected styling.
- Fixed detail hover cards capturing the mouse and causing conversation-list flicker or preventing click-to-switch, retaining delayed dismissal and lifecycle protections.
- Clicking or renaming conversations preserves list position. Dragging before/after entries changes and saves order, restored after restart. Lists without saved manual ordering use creation order; new conversations still enter the Recent display range.
- Fixed buttons consuming bubbling MouseMove events and preventing actual dragging, starting drags during the preview phase instead. Added five real-mouse drag-and-drop acceptance cases covering quick-chat, regular-chat, and work-conversation ordering, plus moves across projects and chat sections.
- Regular chats in the full window save under Default section or a specified chat section; only chats created in the quick window enter Quick chat. Previously misplaced regular chats automatically return by stable ID, retaining history and queues.
- Recent in chat mode shows the current companion's regular and quick chats. Both modes support creating and renaming sections; chat sections show conversations directly, while work sections show project folders. Fixed groups also save display names per mode.
- Work projects can enter only work sections, correcting historical invalid section ownership. Personal and quick chats retain their companion and originating-window ownership restrictions.
- Usage-matrix date cells and edge placeholders now share pixel alignment, preventing layout rounding from producing alternating row/column gaps.
- Updated README, configuration onboarding, and usage documents; added regressions for ordering, recovery after save failure, section ownership, and historical migration.

## [0.9.1](https://github.com/Eason4869/deskpet/releases/tag/v0.9.1) — 2026-10-19

Patch update with input/preview fixes, stronger terminal approvals, MCP compatibility, and a redesigned README. Provides Windows installer/portable packages and macOS Apple Silicon/Intel previews; Mac retains its separate interface and feature boundaries.

- Fixed a lock-inversion deadlock between session-state notifications and concurrent queue reads, preventing occasional hangs on first or subsequent sends. Sessions and queues share one synchronization boundary, with a deterministic two-thread regression.
- Fixed terminal commands potentially bypassing sensitive-operation confirmation through PowerShell aliases or .NET file APIs under Request approval / Approve for me. All Agent terminal code requires individual manual confirmation and does not start after rejection or cancellation, retaining AppContainer isolation.
- MCP preserves root-level input-schema definitions, references, and constraints, handles parameterless tools correctly, and reports unsupported root structures explicitly during registration. OpenAI and Anthropic use the same independent snapshot.
- MCP tool business failures and JSON-RPC request errors retain bounded details for correction and retry. HTTP user cancellation preserves valid sessions; stdio cancellation cleans up desynchronized streams. Connections are cleaned up only when the connection or protocol actually fails.
- Fixed caret movement after typing URLs, edited URLs still opening the old address, and separately clickable web icons. Autolinks retain bare addresses; explicit Markdown links still allow independent label editing.
- Fixed clipped tool icons during processing. Unsent images support click and keyboard previews, preserving drafts and removal actions in full and quick chat.
- Rebuilt README with a unified blue-purple style, real full-interface screenshots and companion cards, narrow-screen assets, and updated permission, platform, and installation guidance.
- Fixed an onboarding smoke-test timing race: wait a bounded time for buttons to become ready before acting, rather than declaring failure after fixed waits shorter than the validation interval.
- Streaming UI regressions first confirm target content and actual layout are ready, then check existing reasoning/content position constraints with precise geometry diagnostics, without relaxing display-order assertions.
- Shared MCP regressions directly validate the cross-platform tool catalog and runtime. Windows workspace integration remains separate, avoiding construction of a Windows-only file Agent in macOS tests.

## [0.9.0](https://github.com/Eason4869/deskpet/releases/tag/v0.9.0) — 2026-10-19

Windows feature and interface update, advancing from development version 0.8.0 to 0.9.0. Provides Windows installer/portable packages and macOS Apple Silicon/Intel previews; Mac retains its separate interface and feature boundaries.

### Unified window and conversations

- Combined chat, Settings, Plugins, and usage statistics into one main window with a client top bar and fixed icon navigation. The conversation sidebar starts expanded and adapts to small screens.
- Project and conversation sidebars collapse independently. Page/layout changes preserve drafts, reading positions, and tool state. Fixed leaking task text when the sidebar was hidden and visual-tree lifecycle issues in hover cards when switching conversations.
- Work summaries pin or open temporarily according to available space; pinned icons remain highlighted. Automatic speech sits between avatar and title. Unified rounding, dark tooltips, icon/text alignment, and search-field line heights; removed Alt dotted outlines and persistent post-click focus borders.
- Added fading companion artwork to chat history. Reasoning and execution records use companion avatars and preserve the identity used to generate each reply. Added jump-to-bottom and fixed delayed scrolling overriding reading positions.
- Uploaded and AI-returned images use floating in-window previews with background blur, zoom, drag, blank-area click, and Esc dismissal. Remote images load on demand with size, timeout, and cancellation limits.

### Work permissions and tools

- The work input supports Request approval, Approve for me, and Full access, defaulting new configurations to Approve for me. Choices save per conversation with legacy compatibility. Full access requires confirmation; plan mode continues enforcing write restrictions at the tool layer.
- Added Windows AppContainer workspace-terminal isolation and a read-only tool catalog. Isolation failures stop execution; independent-review failures fall back to manual approval. GitHub reads/searches enter network approval; MCP and GitHub writes retain separate confirmations.
- Fixed sandboxed PowerShell failing to discover basic command modules automatically on some systems by explicitly loading system assemblies, preserving directory, network, and process isolation.
- File-delivery cards, net changes, and encrypted summary snapshots support reopening from history and controlled undo. File actions retain system open, Save As, reveal, and copy entries.
- Added main-window tool tabs: file and HTML/Markdown/source previews, a ConPTY PowerShell terminal, browser, temporary side chat, and change details. Supports split and full-tab views with automatic narrow-window adaptation.
- The browser reclaims its own temporary profiles after process exit and conservatively cleans expired, unused cache directories it owns.

### Plugins, statistics, and settings

- Plugins manages local skills and MCP. Skills support import, search, details, persistent enable/disable, and removal, and can be read on demand by work requests. MCP first shows a list, enters configuration only for add/edit, and actively connects after saving.
- Added a local text-model usage ledger with overview, full-week heatmap, 7/30-day history, filters, and conversation rankings. Distinguishes actual, estimated, unknown, and true zero values; includes auxiliary requests and excludes ASR/TTS/Live.
- Plugins, Statistics, and Settings adaptively center their content. Settings uses category navigation and search; About now uses a circled exclamation icon.

### Desktop companion and onboarding

- Explicit left/right running from the wheel or context menu now moves the companion, stopping at screen edges. Dragging, pausing, or changing action interrupts immediately; locked positions demonstrate in place. Position saves after stopping.
- Fixed model-list retrieval updating controls across threads when no UI synchronization context existed. Results, failures, and loading state return to the owning UI thread.
- Configuration onboarding's work-permissions chapter matches the actual three-level approval controls, retaining stable step IDs.
- Added an opening explanation and congratulations page to configuration onboarding, with unified rounded corners and custom title areas. Supports skipping a step/chapter, continuing later, or ending onboarding. Lightweight confetti plays once; Finish onboarding opens full chat.
- Rewrote README and offline onboarding to clarify Windows/macOS differences and development-branch status. App connections and the plugin-package runtime remain TODO items.

## [0.8.0](https://github.com/Eason4869/deskpet/tree/v0.8.0) — 2026-10-18 (dev branch tag)

Pushed Agent and interaction improvements to **dev** and created the **v0.8.0** source tag, without a GitHub Release or replacing the current preview. Adds a complete tool-execution chain over 0.7.0, warranting a minor-version increase.

- Dragging, click interactions, and the action wheel remain usable during thinking, work, and waiting, automatically matching thinking, checking results, dejected, and waiting actions.
- Fixed formatting continuation after spaces/newlines following inline Markdown in the input, and draft newlines at block ends.
- Connected individually confirmed terminal commands, asynchronous approvals, capability provenance, context budgeting, trusted instructions and skills, explicit MCP, independent read-only subtasks, execution records, and manual project facts.
- Completed background-session speech stopping, volume synchronization, and save-warning gates. Configuration onboarding expanded to 17 chapters and 130 small steps.
- This version delivers individual confirmation mode as selected by the user, with the system sandbox disabled. Three-level approval, system isolation, and quick switching remain separate future work.

## [0.7.0](https://github.com/Eason4869/deskpet/releases/tag/v0.7.0) — 2026-10-18

Consolidates conversation, workspace, and editor interaction, increasing the minor version to **0.7.0**. Provides Windows installer/portable packages, macOS 14+ Apple Silicon and Intel previews (DMG and ZIP for each), source, four companion skins, v2 authoring templates, and SHA256 checksums. Mac previews lack Apple Developer ID signing and notarization and do not include this iteration's new Windows sidebar, direct Markdown editing, 118-step configuration onboarding, or native file Agent.

### Conversations and projects

- Regular and quick-chat context/history belong to each companion; switching companions shows only the corresponding personal records. Work mode continues sharing projects, work history, and a separate work prompt.
- Removed the Conversation / Default project structure. Added a fixed Quick chat group after custom sections and before Recent. Both fixed groups collapse/expand and have no editing entry.
- Reopening a closed quick-chat window creates a new conversation without context. Old records remain accessible in the companion's quick-chat list.
- Regular chat cannot select projects and hides work-only plan/goal menus; quick chat has no mode-switch command. Work drafts show a project selector. Fixed the missing project bar on first opening the full window, and display/ownership issues after mode changes.
- Added lightweight pin/unpin and archive actions to sidebar conversations, with hover descriptions. Improved project dialogs, section management, Logo alignment, and bottom Settings-button placement.
- Chat deletion now uses a rounded in-app confirmation without a system sound. Cancel preserves data; save failures show matching errors. Workspace closure uses an asynchronous flow.

### Editing and follow-up during execution

- Input directly edits formatted Markdown by default, hiding syntax while preserving nested lists and multiparagraph indentation within lists. Supports native selection, editing, undo, and redo. Plain-text mode retains raw markup; switching preserves drafts.
- Added an Editor group to Work settings: predictive messages, plain-text editor, context usage, Enter/Ctrl+Enter to send, and queue/steer. Globally saved for full and quick chat; global preferences on project pages are read-only.
- Predictive messages default to on, generating draft suggestions from recent conversation without sending automatically; can be disabled.
- User bubbles shrink to content, using the old width as a maximum. Input, user messages, and assistant replies share icons plus clickable links; GitHub uses its Logo and other sites a globe, removing domain capsules.
- Steering appears immediately as a user message with activity below, without an interruption notice. Started operations continue and receive new requirements at the next adjustable boundary; unexecuted old tool calls receive skip results and the model replans. The task and cancellation token stay the same.
- Fixed shimmer animations and icons for thinking/editing/running; hid drag hints when only one message is pending.

### Work settings and reliability

- Removed project-command configuration and its unused editor. Desktop clients do not load old command authorizations or expose unconnected MCP, automatic sub-Agent execution, or GitHub writes.
- Context budgets follow model configuration, showing a fallback input only when capacity is unknown. Displayed usage and actual requests use the same budget policy; hiding usage does not disable automatic compaction.
- Work prompts are configured separately and shared across companions; regular chat continues using companion personalities.
- Session data saves by stable ID with legacy-directory migration. Kernel sessions use encryption and atomic saving, preserve damaged data, and do not overwrite originals on save failure.
- Fixed queue, stop, regenerate, and resume-task lifecycles. Moving conversations synchronizes working directories, permissions, goals, and task identity.
- Retrieved models are added individually, removing multiselect checkboxes; capacity and Chinese display names save with configuration.

### Onboarding, appearance, and resource use

- Rebuilt configuration onboarding into 17 chapters and 118 small steps covering cloud TTS toggles, models/voices, device tests, window resizing, section/project/conversation creation, project selection, and editor preferences. Supports contents navigation, skipping, and resuming, recording saving and actual testing separately. Updated offline onboarding.
- Fixed gray edges in Ebao's old heart gesture while preserving original proportions, colors, and frame order.
- Idle message pumps stop polling and burst updates are merged. Action decoding caches hold at most three actions; skin changes release old caches.
- Static bubbles no longer repeatedly move or rebuild geometry. Closing hover cards/conversations releases visual references, subscriptions, and timers. Reused Markdown parsing and removed double previews and obsolete draft caches.
- Build scripts use UTF-8 BOM to avoid Windows PowerShell 5 misreading Chinese app names. Unified product/assembly versions across Windows and shared desktop projects.
- Avalonia starts in chat mode on first launch; Mac does not inherit Windows' default file-work mode.
- Rewrote README around user-facing features, installation, configuration, and platform guidance. The Mac client does not claim the new Windows interface or complete file Agent from this iteration.

## [0.6.3](https://github.com/Eason4869/deskpet/tree/v0.6.3) — 2026-10-18 (dev branch tag)

Pushed **dev** and created the **v0.6.3** source tag, without a GitHub Release or official release attachments.

- Chat-mode reasoning is now a gray single line, collapsed by default and expandable for full reasoning; collapses when content starts and retains a gray entry after completion.
- Work mode no longer displays reasoning text. Single-line actions map to Editing / Running / Viewing / Restoring / Planning / Processing with a shimmer. Completed tools temporarily show in black single lines; the process area collapses on completion, retaining only final results and file-change previews.
- New conversations belong to the selected sidebar project, falling back to the most recently opened project. Conversations can be dragged to other projects while preserving history and queues.
- Rebuilt the new-project dialog as Create project, with project name, source-folder panel, folder dropping, and a dark Create button. Aligned sidebar + and count columns.
- Fixed project-tooltip flicker through dismissal debounce, staying open within the card, and avoiding reopening on refresh.
- Fixed blank space at the bottom of maximized full chat.
- Typing `/` or `@` in the input opens a built-in tool menu with filtering, keyboard selection, and execution.
- Fixed task records being cleared by the task panel reading/writing the wrong workspace path; workspace retention after closing chat; speech/regenerate/resume permanently pausing queues; and path traversal in attachment temporary filenames.
## [0.6.2](https://github.com/Eason4869/deskpet/tree/v0.6.2) — 2026-10-18 (plan branch tag)

Only pushed the **plan** branch and created the **v0.6.2** source tag, without a GitHub Release or official release attachments.

- Completed a Codex + OpenCode-style Agent kernel: Session/Inbox/Step, context recovery, unified tool execution, permission approvals, stable CallId, execution declarations, and auditing.
- Integrated Provider capability descriptions, `build/plan/explore/general` Profiles, queued sub-Agent lifecycles, and MCP HTTP / stdio tool discovery and calls.
- Multiple projects and conversations now switch through tabs in one window. Background workspaces, history, queues, and drafts remain independent; messages can continue after completion.
- Changed the default project name to Default project, supporting add, rename, archive, restore, and permanent deletion. Conversation titles are generated by the current model, with right-click manual rename/archive/delete.
- Sidebar project/conversation titles wrap fully; expand/collapse uses open/closed folder icons. Fixed black project-hover flicker and bottom overflow in maximized full chat.
- Markdown links in input previews, user messages, and AI output use a unified clickable rounded style; input supports live Markdown preview.
- Improved AI file-output cards with open, Open with, reveal in Explorer, copy path, copy contents, and Save As.
- Fixed subsequent sends after completion, MCP Schema, stable sub-Agent IDs, Provider tool gating, and packaging long paths.
- Final validation: 325 .NET 10 shared regressions passed; targeted Windows UI, Agent, queue, documentation, packaging, and real-updater regressions passed.
## [0.6.1](https://github.com/Eason4869/deskpet/releases/tag/v0.6.1) — 2026-10-16

Published Windows installer/portable packages, macOS 14+ Apple Silicon and Intel previews (DMG and ZIP each), source, four companion skins, v2 authoring templates, and SHA256 checksums. Mac previews are only ad-hoc signed, without Apple Developer ID signing or notarization; local-file Agent and app snapshots are Windows-only.

- Quick and full chat share user messages, half-width light-blue bubbles on the right, and AI content without bubbles; content and user input render Markdown natively.
- Important reasoning notes use body-text color, tool records light gray. The process hides when final output begins; copy, speech, and More icons appear on completion. More supports regenerate, plain-text copy, Markdown export, and process records.
- Processes default to a single Thinking / Processing / Editing / Running status. An expansion arrow appears as a gray > on hover and opens details; it hides when final results start. User-message hover shows send time and copy. AI replies add live Thinking time / Processing time, fixing the label to the mode at send time and retaining elapsed time in history.
- Added file/folder attachments, app-window snapshots, persistent goals, and read-only plan mode at the input's lower left. Files/images support paste, + upload, and drag-and-drop; Chinese edit menus share a rounded light style. Image thumbnails and file cards can be removed before sending; attachments persist with queues and encrypted history.
- Windows includes a native OpenAI/Anthropic Agent tool loop for listing directories, reading, searching, writing, exact replacement, creating folders, and opening directories. Defaults to full system-permitted access to local directories such as Desktop, switchable to working-directory-only, with pre-change backups on by default. Requires no external CLI, Python, or Docker. Plan mode blocks writes at the tool layer.
- Added unified rounded avatars for all four built-in companions in character menus, chat, Settings companion selection, onboarding, and Live thumbnails; README character profiles also use avatars. Hover/keyboard focus uses gray rounded backgrounds instead of blue focus frames. Sent attachments display separately from user-text bubbles.
- Fixed clipping and positioning differences in the lower-right main button for narrow windows, long model names, and recording states. Mac retains chat, Markdown, attachment, and interface compatibility; the native file Agent and app snapshots are Windows-only in this iteration.

- Renamed detailed AI configuration to Model configuration and added Work settings for permissions, working directory, plans, goals, backups, turn/time limits, and process details. Chat mode uses a chat icon and forbids the Agent; work mode uses a writing icon. Tab switches modes.
- The + menu now directly selects files/folders and shows the topmost-app snapshot icon. Directory attachments do not change work permissions. Scrollbars use thin gray rounded thumbs, darker on hover and draggable. Focused input retains a white background and placeholder; fixed gray focus overlays and disappearing hints.
- Windows automatic update checks now run every four hours, retrying failures after ten minutes, retaining download, verification, and idle-install flows. Mac still updates manually through architecture-matched download entries.
- Windows and Mac onboarding expanded to 17 steps, content revision 2. Existing 0.6-series users see new features once from step 15; first installs and cross-series upgrades still start at the beginning. Closing, skipping, or completing prevents repetition.
- Installer/portable packages now include conversation-Agent guidance, third-party licenses, and rounded-avatar previews. Source packages exclude local working directories, credentials, and signing private-key files.

## [0.6.0](https://github.com/Eason4869/deskpet/tree/v0.6.0) — 2026-10-16 (development tag)

Code and tag only: no GitHub Release or public installer distribution for this version. The latest public Release remains Windows 0.5.6.

- Fully inherits 0.5.6 Windows behavior and fixes for onboarding upgrades, transparent-window recovery, recording faults, input alignment, and atomic Settings-replacement retries. Adds Mac adaptation without introducing separate conversation logic.
- Completed companion, chat, Settings, onboarding, Live, menu-bar, and platform services for the Avalonia / .NET 10 Mac client. Built self-contained macOS 14+ arm64 and x86_64 previews separately.
- Mac previews are ad-hoc signed, without Apple Developer ID signing or notarization; preview packages are provided for both architectures. Actual microphone authorization, listening quality, multiple displays, and login/restart remain subject to manual acceptance.
- Windows builds now use `DeskPet-Windows-Setup-0.6.0.exe`. Mac package names identify platform, version, CPU, and preview; update entries select only attachments matching the local platform and CPU. 0.6.0 begins a new major/minor series, showing new onboarding once when upgrading from 0.5.6.

## [0.5.6](https://github.com/Eason4869/deskpet/releases/tag/v0.5.6) — 2026-10-16

Published Windows installer, portable package, source, four companion skins, and the complete v2 template. macOS proceeds separately; 0.6.0 adds adaptation with code and tag only. 0.5.6 contains no Mac installer.

- Officially delivers 0.5.5 dev interactions: a 40% stumble chance for four companions, four-sector skills, two named signature moves, one-of-four hover actions, idle naps, and 90–180-second walking intervals, reserving a fifth holiday-action interface.
- Retains old/new feeding, happiness, two signature moves, and standing/sitting nap sets, randomly selected per trigger. Pick-up and normal/stumbling landing are selected in pairs. All four landing variants share a 0.3-second fall of 24 pixels at standard size, with no extra lift on pickup. Uses 8 FPS without frame blending or catch-up skipping.
- Configuration onboarding compares major/minor series numerically. Entering a higher series resets steps and opens once. Same-series patches do not trigger it; closing/skipping/completing prevents repeats. Downgrades retain the highest series; legacy configuration migrates as 0.5.
- Recording-device failures end dictation/conversation recording, release devices, and show reasons, preventing a stale recording state after device loss or background threads waiting on themselves.
- Fixed transparent companions sometimes no longer receiving mouse clicks after being hidden/restored. Reuses the same window, stops action rendering while hidden, and enables complete click-through. Dictation waveforms align with the 32px main button, removing a 1px shift on switching.
- Windows update filtering accepts only standard package names for this platform, rejecting Mac, Linux, source, template, and skin attachments. 0.5.6 still uses `DeskPet-Setup-0.5.6.exe`; the app/updater already support `DeskPet-Windows-Setup-版本.exe` from 0.6.0 onward.
- Updated README download versions, skill names, four-sector wheel examples, roadmap, and cross-version upgrade guidance. Installed users on stable 0.5.4 or earlier must manually install 0.5.6 before using built-in updates, or manually install 0.6.0 or later directly. Portable ZIP names remain compatible, preserving existing `data`.

## [0.5.5](https://github.com/Eason4869/deskpet/tree/v0.5.5) — 2026-10-16 (dev snapshot)

Only commits dev and a version tag; no Release or 0.5.5 installer distribution. macOS migration is in progress; the current desktop frontend still lacks some windows and cannot compile, and is not ready for release.

- Added probabilistic stumbling after drag-and-drop for four built-in companions: 40% chance after actual movement, with a ten-second cooldown. Can be disabled/interrupted; custom skins retain normal landing.
- Added one named signature move each: Ebao's Heart encouragement, Dafeiyu's Bubble tour, Marfu's Glowing spell, and Xiaobei's Holographic keyboard. The skill wheel now has four equal sectors; original actions display as Tsundere head turn / Tail-wag cheer / Wand glow / Tai chi aura.
- Redrew each companion's four skills, happiness, pick-up, normal landing, and stumbling against original artwork, also fixing Ebao's jump: 33 strips, 264 frames. Unified proportions, transparent edges, and positioning, using eight poses at 8 FPS without frame skipping to catch up after delayed refresh or frame blending.
- Hover chooses equally among jump, greeting, and the two named signature moves, triggering once per entry. Reserved an interface to add a fifth holiday Easter egg by companion and local date.
- Added idle naps, on by default: after five minutes of system inactivity or 15 minutes without companion interaction, a 70% chance per minute. Does not trigger while busy, hidden, paused, or onboarding. Waking resets the timer; can be disabled.
- Retained old/new sets for snacks, happiness, naps, and both signature moves, choosing one per trigger; post-feeding happiness follows the same version. Naps retain the old sitting and new standing poses.
- Pick-up and landing versions are paired. Normal/stumbling landings after release share a 0.3-second fall curve, moving 24 pixels at standard size, scaled with size and bounded by the screen bottom. Pick-up adds no upward movement.
- Reduced Occasional walks intervals to 90–180 seconds, restarting the wait after interaction, with unchanged speed/distance.
- Began Avalonia / .NET 10 macOS migration: extracted shared business, encryption, and audio contracts, added native Mac platform bridging and a first desktop-interface draft. Official packaging, signing, notarization, and Mac testing remain unfinished.

## [0.5.4](https://github.com/Eason4869/deskpet/releases/tag/v0.5.4) — 2026-10-16

Published Windows installer, portable package, source, four companion skins, and complete v2 authoring templates. macOS is deferred to later versions.

- Redesigned text-model configuration: provider sidebar uses offline vector Logos and vendor-based default names while preserving manual names. Connection settings expand; model lists include search, layered Chinese-name/model-ID display, current-selection markers, and count feedback.
- Adding a provider now uses a vendor picker, followed by Save and fetch model list after entering the Key, multiselecting candidate models, adding them, then saving configuration. Saved connections automatically fetch OpenAI/Anthropic-compatible lists, retaining manual refresh, manual model entry, and Chinese aliases. Switching provider/leaving cancels requests; late results do not modify other drafts. Failed retrieval preserves existing lists.
- Unified speech recognition, speech output, and Live with vendor-Logo sidebars and compact connection settings. Selecting a vendor prefills an editable model name without fetching voice-model lists. Services save independent connections; removed the reuse-text-Key switch, migrating old reuse settings while retaining effective credentials. Speech/Live tests sit on their respective pages, leaving only Save at the bottom; saved multivendor voice drafts restore correctly.
- Updated onboarding model-add flows and scope: add vendors, fetch lists, select models, or add manually after failure. Text steps require actually adding/selecting a model before continuing. Next saves only the current page; voice pages use prefilled models and separate Keys. Speech/Live tests become available after exiting onboarding.
- Fixed Live ending calls when long-reply audio arrived early with over ten seconds not yet played. Plays short audio chunks and distinguishes reply backlog from device stalls, retaining memory limits, immediate interruption, and device-failure feedback. Fault cleanup avoids playback threads waiting on themselves.
- Removed default highlighting of the top sector when opening the middle-button action wheel, retaining hover, arrow-key navigation, and click-to-play.


## [0.5.3](https://github.com/Eason4869/deskpet/tree/v0.5.3) — 2026-10-16 (dev preview)

Development version: only updates `dev` and creates the `v0.5.3` tag, without a GitHub Release, new installers, or skin ZIPs.

- Ebao, Dafeiyu, and Marfu use earlier low-frame-rate original assets and action timing; Xiaobei also uses a low-frame-rate version, without frame blending. Restored returning to idle after roughly 1.4 seconds of a stationary mouse and showing original directional illustrations when movement resumes.
- Added v2 per-action atlas imports and high-frame-rate authoring templates, supporting per-group frame counts, FPS, loops, 16-direction gaze, personalities, and special-action names. Added fourth preset companion Xiaobei with all actions, a male preset personality, and Tai chi aura, requiring no import. High-frame-rate templates are for authors and do not mean preset companions currently use 24 FPS.
- AI configuration supports multiple providers, their model lists, and Chinese display names. Model selection, dictation, and send are grouped at the input's bottom right, increasing output space.
- Pending queues support six-dot-handle drag reordering, translucent overlays, displacement/settling animations, and matching actual send order. Rows use a separate queue vector icon, retaining edit, steer, and delete.
- Fixed brief walks and interactions repeatedly resetting proactive-greeting timers. Expanded preset character lines and added an AI-greeting switch, off by default, using the current model and current companion's recent history. Failure/timeouts fall back to presets; canceled automatic requests can retry after a brief idle delay.
- Enlarged dictation and Live waveforms. Middle-button wheel centers on the invoking cursor, shrinks overall, and adds translucent glass styling. Settings uses lightweight glass cards; increased model-configuration default width and minimum height.
- Removed pet-hover teaching hints and the duplicate actions at the bottom of Companion behavior. New installations default to EbaoDeskPet; upgrades retain existing locations.
- Fixed pet context menus briefly appearing then closing while Settings was open. Menus retain original focus; submenus and outside-click dismissal still work.
- Fixed placeholder misalignment in quick/full chat, aligned to the first input line. Hints hide on focus and return after clearing and losing focus.
- Fixed time-limited jumps briefly returning to their first frame before ending; landing holds the final frame until idle.
- Added complete authoring templates to the repository. Packaging retains user guidance/templates while excluding development acceptance reports, test scripts, debug logs, and obsolete assets. No official packaging was performed in this iteration.
- Preview remains Windows dev source; the macOS version is still being prepared.

## [0.5.2](https://github.com/Eason4869/deskpet/tree/v0.5.2) — 2026-10-15

Development version: pushes `dev` and creates `v0.5.2`, without a GitHub Release or installer uploads. The local machine overwrites the original directory with a local build.

- Cleaned transparent hair edges across all Ebao actions, gaze, and previews. Lightened gray hair outlines using idle/gaze frames as references, emphasizing consistent jump frames. Preserved frame sizes, poses, expressions, and alignment parameters.
- Fixed onboarding context-menu focus: activate onboarding before opening; no focus stealing afterward. Submenus remain viewable and blank-area clicks still close the menu.
- Added a real double-click-to-open-quick-chat eighth step, expanding onboarding to 16 steps. Explained queues and network acceleration separately, added both dictation-ending methods, pause/resume, tray, and record management, with legacy progress compatibility.
- Quick/full chat share send/stop/call main buttons, with dictation immediately left. Reduced the main button to 32 DIP and unified visual centering of send arrows.
- Added per-companion message queues, sequential sending, priority steering, pencil editing, and deletion. Stop/failure pauses queues; restart restores them paused. Unsent queues block update replacement.
- Dictation is independent of AI replies and shows actual microphone waveforms. Stop dictation recognizes into the draft; Send dictation recognizes then sends/queues. Esc or closing cancels.
- Increased Live ring-waveform response and extension at normal speaking volumes in quick/full windows, retaining silence decay and edge limits to prevent clipping.
- Unified onboarding naming as Configuration onboarding. Bottom left is Skip onboarding; right is Skip this step. Dictation instructions now float above the button.
- Reorganized About with a fixed uninstall entry in the bottom bar. Network acceleration defaults collapsed, offering mutually exclusive Direct, GitHub mirror, and Network proxy modes. Mirrors default off, with four built-in addresses sorted by length and a custom option. Test first for status/time, then select a working address. Proxy defaults to http://127.0.0.1:7890. Mirrors apply only to update-package downloads; versions and SHA-256 come from GitHub. Mirror failures or checksum mismatches fall back to direct.

- Fixed first-show top-left flashing in the middle-button wheel by positioning before display, defaulting above the companion and constrained to screen bounds.
- Added one jump per hover; gaze tracking pauses while hovering and resumes on exit, without interrupting onboarding, chat, or explicit actions.

## [0.5.1](https://github.com/Eason4869/deskpet/tree/v0.5.1) — 2026-10-15

Development version: pushes `dev` and creates `v0.5.1`, without a Release or installers. Based on 0.5.0; no frame-rate increase, and README TODO items remain unimplemented.

- Redesigned quick/full-chat input, icons, and hint hierarchy, unified thumbnails and speech toggles, and removed the duplicate top-left pet name. Send uses an arrow; Stop uses a custom red outlined square permanently at the input's end.
- Both quick/full input areas have Live and voice-input icons on the left, with unified send/stop size/strokes on the right; removed chat's mode dropdown. Expanding full chat automatically collapses quick chat.
- Live overlays its initiating window, with compact/large quick/full layouts. Hanging up restores the original draft; closing the host waits for recording/playback release. Fixed waveform overlap in small windows and redrew hang-up/Settings icons.
- Middle-button actions now use a radial wheel with equal gaps and actual character thumbnails. Center category/close, wheel scrolling, arrow selection, Enter playback, Esc or middle-button collapse.
- Combined Companion and appearance with Personality into Companion, with Companion and personality / Companion behavior / Proactive greetings subtabs. Other sidebar entries are AI configuration / Shortcuts / About. Right-click separates quick Appearance switching from detailed Settings, retaining unsaved-content protection and onboarding targeting.
- About's Network proxy defaults collapsed; removed the redundant copy-repository-address button.
- Rebuilt 13-step onboarding covering configuration, dragging, right-click, middle-button wheel, and click interactions. Noncurrent items are grayed/locked; Next is disabled until required fields/actions complete. Set up later skips only the current item; Skip onboarding ends the flow; closing restores all operations. Services validate/save independently for the current item; skipped drafts do not block other services.

## [0.5.0](https://github.com/Eason4869/deskpet/releases/tag/v0.5.0) — 2026-10-15

- Added pick-up, landing, petting, poking, feeding, happiness, naps, and special actions to three built-in companions. Single clicks interact by head/body; double clicks retain quick chat.
- Added a middle-button action panel using the current companion's action thumbnails, short labels, and hover descriptions. Skills offers only snacks, nap/wake, and special actions; Actions retains nine basic groups. Petting, poking, pick-up, and landing still use desktop gestures; happiness follows feeding automatically. The original context menu remains.
- Consolidated Settings navigation into Companion and appearance, Personality, AI configuration, Do not disturb and shortcuts, and About and updates. Text models, ASR, TTS, and Live share four AI-configuration subtabs. Unified forms, password fields, focus feedback, and save areas; configuration switching/saving remain accessible in smaller windows.
- Added six-step floating first-use onboarding highlighting real controls and supporting direct input/interaction. Next is the default primary button; connection steps save before continuing. Supports Later, Skip, progress restoration after closing, and reopening from Settings' bottom left. Existing users do not see new onboarding automatically on upgrade.
- TTS/Live voices save per companion, service address, protocol, and model, with character-appropriate initial recommendations. Switching companion/service restores its selections, retaining manual Voice IDs. Recommendations come from actually supported voices.
- Quick/full chat gained graphical Live entries opening a separate call window with companion avatar, subtitles, and a ring waveform driven by actual input/output volume. Supports hang-up, reconnect, AI configuration, and return to text chat. Closing ends recording/playback.
- Moved quick/full TTS toggles to a speaker icon beside the companion name, synchronized with Settings. Turning off immediately stops speech while retaining text; native Live audio remains call-managed.
- Corrected body positions and foot baselines in basic actions, preserving natural airborne movement in jumping/running, without blended interpolation.
- Manual update checks allow idle chat/Settings windows to exit automatically with the app and restart after installation, protecting unsent text in hidden windows and unsaved settings.
- Chat-history save failures pause automatic installation. Update handoff waits again for history writes, preventing exit with unsaved records; onboarding/action panels also delay it.
- Installed-build detection validates both the uninstaller and metadata in the current directory, preventing stale installation records from choosing installer updates for portable builds.
- Fixed minimized Live calls failing to restore through chat entries; repeated closure waits for recording/playback release.
- Protects unsaved settings before companion switching/import; refreshes personality/voices by stable character ID, avoiding stale content after switching same-name skins.
- Updated installation, daily interaction, AI-configuration, and voice guidance, plus usage flowcharts and onboarding previews.

## [0.4.1](https://github.com/Eason4869/deskpet/releases/tag/v0.4.1) — 2026-10-14

- Fixed 0.3.3 remaining on the old version after Update: GIF previews in runtime packages were rejected by the old updater's safety allowlist, aborting updates and restarting the old app.
- Runtime packages restore static documentation compatible with old updaters. GIFs remain in GitHub README/source packages; local README uses corresponding PNGs. Preserved path and user-data protection checks.
- Packaging regressions now apply a complete ZIP using the actual updater, checking versions, original-app backups, configuration, chat history, and imported skins.
- Fixed TTS / Live voice dropdowns appearing blank after selection by completing editable-dropdown templates; selected, manually entered, and reopened voice values display correctly.
- No animation changes; retains 0.4.0's three-companion gaits and unified speed.

## [0.4.0](https://github.com/Eason4869/deskpet/releases/tag/v0.4.0) — 2026-10-14

- Chat history saves per companion by default, encrypted with current-Windows-user DPAPI and written atomically; supports loading earlier records, Markdown export, and clearing.
- OpenAI / Anthropic text protocols support streaming synchronized across quick bubbles and full chat. Stop/disconnection retains partial text with status labels; incomplete turns do not enter full model context.
- Added Do not disturb, hide on current-screen fullscreen, global click-through, position locking, and four editable/disableable global shortcuts; tray offers recovery entries.
- Retained Ebao, Dafeiyu, and Marfu, showcasing all three in README. Removed ghosting interpolation/frame blending; actions switch directly between original frames.
- TTS/Live offer editable voice-preset dropdowns by vendor/model with short descriptions and manual input.
- Marfu's default personality adds gender-swapped Draco Malfoy traits while preserving custom personalities. Slowed running, used mirrored real frames with clear gait for left runs, and fixed transparent hit testing. Corrected Dafeiyu's backwards-looking left run; unified all three at 0.22 seconds per running frame and 1.76 seconds per cycle.
- Interrupted, stopped, and failed Live subtitles remain marked incomplete instead of entering successful chat context.
- Unified 0.4.0 versions, file descriptions, and author information across the app, updater, and installer. Historical 1.x.x entries gain -beta for distinction.
- Published Windows installer, portable package, full source, and three HD skin packages.

## [0.3.3](https://github.com/Eason4869/deskpet/tree/v0.3.3) — 2026-10-14 (dev development version)

- Added Marfu: blonde hair, green eyes, black-and-green wizard robes, a wand, independent personality, and nine action groups.
- Expanded Ebao, Dafeiyu, and Marfu to 584 frames each; actions use 8× interpolation and gaze 128 directions, preserving durations.
- Adopted rendering callbacks and actual elapsed time, targeting 60 Hz; hidden windows detach rendering callbacks.
- Corrected foot positioning in idle/gaze poses to reduce Ebao's side-to-side jitter; a mouse-center dead zone and shortest-path easing reduce abrupt turns.
- Preserved legacy HD-skin import compatibility; normal builds require no Python or OpenCV.

Only pushed **dev** and tagged **v0.3.3**, without packaging or a GitHub Release upload.

## [0.3.2](https://github.com/Eason4869/deskpet/tree/v0.3.2) — 2026-10-14 (dev development version)

- Added proactive-greeting bubbles, defaulting to every 30–60 minutes with enable/disable and interval controls. Ebao and Dafeiyu use distinct tones; imported characters use their own names.
- Greetings do not steal focus, dismiss after 12 seconds, and open quick chat through Chat a little. Suppressed during chat, Live, fullscreen, hidden, and paused states, without catch-up greetings after resuming.
- Startup/manual checks support automatic download, verification, installation, and restart. Background checks occur at most once per 24 hours, with toggles, progress, cancellation, and idle waiting.
- Installed builds upgrade in place; portable builds replace runtime files while retaining data. The updater waits for the old process and verifies again; failed portable replacements restore originals.
- Background updates show cancelable countdown bubbles. Unsaved settings defer installation; handoff blocks new sessions and rechecks activity. Receipt-write failures still attempt restart.
- Improved Settings navigation, rounded inputs, switches, chat toolbar, quick chat, and About; updated actual-interface screenshots and flowcharts.
- Fixed context menus failing to close after clicking nonactivating areas such as Desktop.

Only pushed **dev** and tagged **v0.3.2**, without packaging or a GitHub Release upload. Development tags are not recognized as updates by the stable-version updater.

## [0.3.1](https://github.com/Eason4869/deskpet/releases/tag/v0.3.1) — 2026-10-14

- Unified the app name as **桌面伙伴** and renamed the repository **Eason4869/deskpet**. About uses the fixed, clickable new address.
- Regenerated a blue-and-white LOGO from Ebao's avatar, unifying EXE, tray, window, and installer icons, with a 16–256px multiresolution ICO.
- First launch and the skin list default to **Ebao**, with Dafeiyu second, preserving existing selections and independent personalities.
- Fixed Gemini Live misclassifying binary JSON frames as Unsupported format; supports text, binary, and fragmented messages.
- Clear temporary subtitles after Live ends, preventing old-character subtitles when switching companions or to staged voice mode.
- Full chat and quick bubbles gained a **TTS output** toggle saved with Settings. Turning off immediately stops speech/synthesis, retaining text replies.
- Installer upgrades preserve personal data and migrate enabled autostart and old-name shortcuts for this installation directory. Runtime ZIPs contain only explicit release files.
- Rewrote README with usage-flow diagrams and actual-interface screenshots.

Version numbering changes to 0.3.1; old 1.3.x users must manually install once. Old public Releases were removed as requested; the entries below retain development history.

## 1.3.1-beta — 2026-10-13 (historical version)

- Fixed update downloads potentially lacking TLS 1.2 when opening About and updates immediately after launch before any chat. The update client initializes HTTPS itself without depending on chat connections.
- Added a standalone TLS-initialization regression. Installation, proxy editing, companion skins, and personal data remain compatible with 1.3.0.
- GitHub attachments use unique English filenames with Chinese labels, preventing duplicate names from platform filtering of Chinese filenames. Publication checks match actual attachment names.

## 1.3.0-beta — 2026-10-13 (historical version)

First public GitHub release.

### Added

- Windows EXE installer: Chinese wizard, per-user installation, Start-menu entry, optional desktop shortcut, and uninstall entry.
- Enable autostart in Settings: starts at Windows login, off by default; disabling revokes it immediately.
- Models, ASR, TTS, Live, and GitHub updates each have editable proxy addresses, defaulting to 127.0.0.1:7890. Reusing text-service settings also reuses its proxy.
- About links to the official repository, opening in the default browser, with address copying.
- GitHub Releases version checks and update downloads prefer installers, with progress, cancellation, and integrity verification.

### Changed

- In-place installer upgrades retain configuration, imported skins, and downloads under data. Uninstall retains personal data and clears autostart for this installation directory.
- Removed repository-address editing. Old custom addresses normalize to the official repository, maintaining a consistent update source.
- Running apps block installation/uninstallation from overwriting the current app and prompt users to exit first.
- Centralized version, author, and repository information; added installation, build, skin-import, and chat-configuration guidance.
- Installer builds update the SHA256 manifest. Disabling autostart removes only values targeting the current app, protecting other installation locations.

### Release contents

- EXE installer, Windows portable ZIP, source ZIP, two HD skin packages, and SHA256 checksums.
- Local UI, registry, and installation flows verified with independent test data. Voice quality with actual online model accounts still requires user configuration and confirmation.

## 1.2.0-beta — 2026-10-13 (local development version)

- Companion skins bind independent personalities and conversations. Ebao uses the specified white-haired young-boy personality; imported skins support system_prompt.
- MiMo ASR/TTS conversation-audio protocols; OpenAI / Anthropic text protocols, common service presets, and independent proxy settings.
- Gemini Live real-time voice, subtitles, interruption, and stop cleanup.
- Overhead quick-chat bubbles share the current companion's conversation with full chat.
- Left-navigation Settings, combined tray-visibility checkbox, skin templates, and import tutorials.
- Fixed retaining keys across service domains, Live mode conflicts, download-temporary-file protection, and version comparison.

## 1.1.0-beta — 2026-10-13 (local development version)

- Text chat and ASR → model → TTS voice flows.
- Independent service settings, Windows user-key encryption, immediate volume control, stop, and conversation clearing.
- Fixed recording-buffer order, volume changes while waiting for synthesis, and audio cleanup.

## 1.0.0-beta — 2026-10-13 (local development version)

- Standalone HD Windows desktop companion with built-in Dafeiyu and Ebao.
- Each set has 73 frames at 480×520, nine action groups, and 16 gaze directions.
- Transparent windows, dragging, walking, mouse tracking, action menus, tray, and configuration saving.
- Supports importing transparent PNG sprite sheets and pet.json skin templates.
