# Dialogs, notifications and proactive reading

Baseline: `dev` / `v1.0.2-dev.2`, commit `53504bd`.

## Dialog and guide changes

- Cache maintenance uses the approved compact layout, borderless 20-DIP rounded frame, horizontal total, three categories, full preservation policy and fixed action area. The default height follows its translated content; constrained screens scroll the body.
- Maintenance/uninstall confirmation, provider and language pickers, shortcut capture, import/download/preparation progress, goal and channel editors, image retry/result windows, detached agent tools and in-app errors use the same dialog family. System file pickers, bootstrap failures and primary windows retain their own presentation.
- Review requests appear centered over the actual operation window. Closing denies the displayed request; switching or closing a view does not transfer a decision to another request. Concurrent requests and alternate views retain request ownership.
- The guide's inert approval exercise targets a real conversation approval window. It executes no command and highlights the real review controls. Long guide titles and descriptions scroll with the body; the action footer stays accessible.
- Short progress labels distinguish `后台继续`, `取消下载`, `取消导入` and `取消中…`.

## Tray cards and audio

Completion reminders use passive bottom-right cards with one destination action, a close control, and an eight-second lifetime paused during interaction. They do not reveal a hidden pet or take focus. Very long messages scroll within a compact body.

Four approved original marimba cues cover completion, review/input needed, permission and actionable failure. Images and companion preparation/import reuse completion; ordinary clicks, saving settings, historical results and user cancellation stay silent. A global sound preference and application volume control playback. Sounds yield to speech, recording and live audio, use bounded event deduplication and a short cooldown, and do not queue a burst.

DND cancels active/pending notification audio and blocks new cues. Leaving DND does not replay past events. Device errors leave the underlying workflow available.

## Proactive greeting speech

Greeting settings are ordered `主动问候` → `自动朗读` → `AI 问候`. The independent auto-read preference defaults on, and only speaks when global read-aloud is on, volume is nonzero, DND is off and no other conversation/microphone/live audio owns output.

Preset, AI and automatic screen-aware greetings speak their actual visible text without another model request or a chat-history entry. Manual screen diagnostics stay silent. Dismissal, replacement, companion changes, disabling reading, DND and shutdown cancel speech. Automatic bubble expiry waits for speech, bounded by a 45-second speech lifetime.

## Validation and limits

Regression coverage includes real Windows dialog ownership, focus, cancellation and cache operations; passive-card position/expiry; pending and playing sound cancellation; actual preset/AI/screen greeting hooks; request migration; Chinese/German constrained guide layouts; and the portable desktop headless suite. Embedded language packs retain protected historical text and pin new copy separately.

Final local verification: all 17 Windows regression targets passed; the complete portable desktop suite passed 14,775 UI/resource/animation assertions. Focused native checks also passed for tray cards (17), import cancellation (6), notification audio priority (4), image tasks (55) and onboarding/preparation (47). The complete localization audit passed with 5,058 resources and zero errors; language-pack snapshots, build compile-only checks and resource-provenance tests passed. The Windows preview executable was built successfully.

Real macOS audio devices, cloud-provider TTS and physical mixed-DPI multi-monitor acceptance still require their corresponding environments. Tests use controlled audio/capture providers; they do not represent authenticated platform-account acceptance.
