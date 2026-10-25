# 0.11.0-dev.2 live language acceptance

Base: live GitHub dev and v0.11.0-dev.1 both 574cad11ef353c2c05e18d07910b549c03c25726, verified 2026-10-26.

## Stage 1 — catalog and persistence foundation

- WPF LanguagePackTests baseline: 320 passed.
- LiveLanguageTests: 15 passed (C# framework and portable).
- UiLanguageChangeTests: 6 passed (C# framework and portable).
- Portable/Desktop builds: existing CS0649/CA1416/nullable/obsolete warnings recorded, no new API suppression. Network vulnerability-check NU1900 is a local network limitation.
- The legacy language entry point still saves pending preference in this stage. UI hot switching and restart-copy removal follow in stage 2.
- New focused Windows/macOS CI is registered separately so core failures do not wait for the full UI/installer suite.
- No macOS real-device acceptance, merge, tag or release claimed.

## Stage 2 — existing controls and guide

- WPF: live UI across 13 catalogs 28 assertions; main app shell 86; guide bookends 49; language pack embedding/startup 320.
- Avalonia: same windows/services and dirty text/selection plus navigation in 13 locales 67; guide preparation 34; existing Japanese language UI 23.
- Data-management Windows CI exposed a busy-gate race at the fixture's immediate deferred-save retry. The fixture now verifies busy rejection and drains its own queued relationship/keepsake work before idle retry; 72 assertions pass locally. Production busy rejection was retained.
- Four original locale packs retain CRLF per .gitattributes; the other nine use canonical LF so source and embedded SHA256 are identical on CI.
- Restart controls/notices are removed from production language switching. Three unused historical keys remain explicitly retired to preserve immutable translation evidence; relationship recovery restart remains available.
- This stage does not yet establish request language snapshot propagation or real macOS device/input-method acceptance.
## Stage 3 — request snapshots and final review

- Shared catalog/core: LiveLanguageTests 18 (including weak-owner collection and draft ownership); UiLanguageChangeTests 7 (including opaque high-precision JSON preservation); RequestLanguageSnapshotTests 14. Framework and portable targets pass.
- WPF live UI: 58 assertions across all 13 catalogs, editable image defaults/custom drafts, original icons, headers/tabs and dropdown identity. GuideBookendTests 52: same window/position/progress, real Apply/Continue preserves unknown JSON and unrelated drafts, and failed saves remain on the page.
- WPF full English UI audit: 197 captures and 7,643 bounds/control checks. General settings at 700px and guide introduction visually inspected. Native action palette smoke 138; its pre-existing missing keyboard-focus highlight was corrected with the existing hover styling.
- Remaining persistent helper captions were reviewed independently and migrated: Work/Connection/Image settings, relationship rules/statistics/toggles, About routing choices, Plugins and Usage headings/filters/metrics. WorkSettingsTests 67; UsageStatisticsUiTests 68; ImageProviderProtocolTests 85.
- CI failure reproductions now pass locally: ComposerPaletteTests 4, ShortcutHelpTests 52, PermissionUiSmoke 51, DevReviewRegressionTests 14, DraftConversationTests 9, DataManagementUiTests 72. The native data fixture ends its visible-frame keepsake producer before draining and retrying, then restores visibility; production busy admission is unchanged.
- Avalonia full UI/resource/animation suite: 10,102 assertions at the pre-final-helper snapshot; updated focused live UI 67, onboarding preparation 34 and maintenance default sequence 313 pass. The last fixture requires native workspace-owned junction creation on Windows; a sandbox denial was separately identified and rerun outside the sandbox.
- Actual Windows app and standalone updater candidate compile successfully. Updater includes the new catalog/message dependencies and retains only embedded locale resources.
- Review findings addressed: redundant full guide save, translated editable-default identity, helper labels, and concurrent localized-notice reads. No new analyzer suppressions or changes to the baseline warning policy.
- Stage-2 focused Windows/macOS CI passed; full CI exposed the issues recorded above. The first repair round failed. Stage-3 source and refreshed localization provenance are submitted for the second repair round; results must be checked against its exact commit.

## Acceptance boundary

No merge, tag or binary Release has been created. macOS real-device menu/focus/IME behavior, Windows physical IME composition, real paid ASR/Live/Agent services, and speech voice audition remain manual acceptance items; fake transports establish request-language continuity without issuing paid requests. Historical conversations, user prompts, plugin/tool output, and captured/exported documents retain their original text. Update/data/relationship recovery restart mechanisms remain available; language switching no longer asks for restart.

Additional final checks: German dark WPF audit passed 206 captures / 7,878 checks, including visually inspected 700px General and Work layouts. AboutSmoke 23 and native TrayInteractionTests 22 passed. Standalone updater binary verified against all 13 exact source catalog byte hashes, with no artwork resources and within the helper code-size budget. Six duplicate image-default display keys are explicitly retired after consolidating the stable default sentinel; catalog bytes remain protected.
