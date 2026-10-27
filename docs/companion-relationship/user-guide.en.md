# Companion home, relationships and keepsakes

[简体中文](user-guide.md) · [Feature entry matrix](feature-entry-matrix.md) · [Schemas and identity](schema-and-packs.md)

This guide describes stable `0.11.1`. The [historical acceptance snapshot](acceptance.md) retains development evidence and platform limits; current binaries and checksums are on the stable Release. Mac packages remain unnotarized ad-hoc previews; CI validation is distinct from manual acceptance on user hardware.

## Open home and find features

Open the current companion information row in the main window’s lower-left avatar menu. Companion home uses the same window and preserves the previous chat draft when you return. It shows the selected companion’s original artwork, introduction, saved relationship, rules and keepsakes. Long introductions and stage messages can expand; changing companions changes the identity, portrait, score and album together.

The left card shows the complete raw illustration for the highest earned stage above the avatar, name and biography. An official companion with no saved relationship starts with introductory first-meeting art; this creates no points, date or acquired keepsake. Custom companions and unreadable new profiles use their own safe portrait fallback, while a same-identity last successful snapshot may remain during a read warning. The saved stage welcome appears after the statistics in the right relationship card.

Windows settings search finds the companion, relationship, cache, history and default-preference entries and navigates to the actual category/subpage. Search does not perform clearing, reset or uninstall. The native interface retains explicit companion-home, General data-management and About/cache entry points; it does not acquire a separate global search feature.

## Growth rules

Scores belong to each stable companion identity; relationship switches apply to all companions. Stage thresholds are `0 / 80 / 240 / 480 / 760 / 1000`, with a maximum score of 1000 and no required daily checklist.

| Eligible event | Award | Daily category limit |
| --- | ---: | ---: |
| Actual encounter or opening that companion’s home | +2 | 2 |
| A supported interaction actually initiated by the user | +1 | 6; at least 20 seconds between rewarded interactions |
| Successfully completed conversation turn | +2 | 8 |
| Successfully completed root work task | +4 | 8 |

The overall daily cap is 20. Failures, cancellation, repeated completion, internal subtasks, automatic actions and background recovery do not add rewards. Work remains attributed to its original companion. Upgrades do not reconstruct scores from old chats or tasks. Growth feedback follows successful storage; failures retain pending records and show a warning.

Ordinary affection notices rise and fade in, remain briefly for about a second, then rise and fade out. Consecutive gains merge. Interaction responses and stage milestones remain readable longer. Notices avoid covering the companion; if there is no room, the popup is suppressed and saved growth remains available on companion home.

Days since meeting counts calendar days from the first encounter. Active days counts days with actual activity. Daily budgets advance through a naturally elapsed local midnight; changing the offset alone or rolling the clock back does not create a fresh budget. Missing a day does not subtract points.

Disabling growth preserves the relationship and stops new growth; enabling it later does not backfill the disabled period. Disabling feedback clears displayed and pending notices, without replay after re-enabling. Disabling AI stage context stops that additional factual context. Explicit persona settings and preferences remain authoritative. Stage welcomes use the existing greeting channel and its holiday/AI priorities. Stage responses accompany supported user actions and do not add a separate reward.

## Preview and save cards

Saved stages remain available at home. A readable gradient sits behind the right-side text, with a restrained stage/holiday ornament below the title and the complete character artwork in the composition. Preview and save use the same frozen image. Export produces the full **800×480 PNG** with the companion name, stage, actual earned date, stage message and valid days-since-meeting at that milestone. Missing or inverted dates do not invent a day count.

Choose a save location after pressing Save. Cancellation writes nothing; failure is reported and can be retried. Exported PNGs are user data and are not registered as disposable cache. Native save-dialog interaction and macOS device behavior require their separate platform checks.

## Holiday keepsakes

A locked preview is not an owned card. The registry supports **Halloween, Christmas, Spring Festival, Lantern Festival, Valentine’s Day, Qingming, Dragon Boat Festival, Mid-Autumn Festival, National Day and Qixi from 2026 onward**. Eligibility uses the actual local event date, trusted official identity and matching scenes, without retroactive acquisition. Wearing the outfit is not required; quiet mode or disabled relationship growth does not erase owned keepsakes.

Actually displaying the companion or opening its home on the eligible date attempts to save that year’s keepsake. Background loading, reading an album and opening a locked preview do not earn one. Deduplication uses companion, holiday and annual occurrence. It grants no extra affection or wardrobe item. A failed attempt can be retriggered by a fresh eligible encounter while it is still that date; there is no cross-date retry or automatic collection of previous years.

An owned card retains its original scene PNG, captured name/date, outfit/scene versions and hash. Later outfit changes, updates or missing source packages do not replace that saved picture. A damaged picture preserves the record and available historical facts, without invented pixels or counts. Future holidays require controlled calendar definitions, registry entries, identity/outfit bindings and verified scenes; an optional companion pack cannot simply declare a new collectible holiday.

## Maintenance and recovery

**About → Clear cache** scans expired updates, abandoned downloads and managed companion history. Only identified, unused candidates meeting the applicable seven-day grace are eligible; history ages from retirement. Current, recovery, active, unknown, legacy and custom resources remain protected. Post-install background maintenance affects only companion history. Linked or changed entries are protected or skipped; results report actual freed bytes and failures/skips. Personal images, attachments, relationships, keepsakes, chats, keys and project data are outside this cleanup.

**General → Data management → Clear all chat history** clears all companion/project transcripts, model-session context and completed-task replay context. It preserves unsent drafts and their attachments, images and retained attachments, model configuration/keys, personas/voices, relationships/keepsakes/settings, project configuration/source files and explicitly saved project facts. It does not retain a hidden copy of chat text. Unsafe or unsuccessful storage operations produce a partial result and preserve related originals.

**View retained attachments** retrieves independently preserved embedded originals after restarting, with distinct empty and access-failure states. **Restore default preferences** resets the declared display/behavior/greeting, quiet/fullscreen/click-through/position, chat presentation/saving, image concurrency/motion/gallery and shortcut preferences. Language, model services/keys, personas/voices, history, images, relationships/keepsakes, project data, update channel and onboarding progress remain. See the exact [reset field table](data-management.md).

Maintenance rechecks busy and pending-save conditions. Cancel, Escape or closing the confirmation performs no operation; admitted work waits for its result. Corrupt or newer-format files are preserved. Relationship/album warnings offer retry or the data folder as appropriate. If a pending queue specifically requires a restart, resolve the reported file issue first; do not delete data to silence a warning. For backup/restore, close the app and retain the entire `data` directory, including `relationships`, `commemorations` and retained attachments.

**About → Uninstall** first opens the app’s confirmation. Cancel, Escape or closing it keeps the companion running. Busy work, save failures and preparation failures also keep it running with an explanation. Once confirmed, a trusted helper waits for the exact application owner to finish closing before invoking the matching registered uninstaller. Portable/unregistered copies show manual program-file removal guidance that preserves `data`. Actual final-installer uninstall behavior has its own acceptance gate.
