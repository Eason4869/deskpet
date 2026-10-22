# Official companion distribution and final 0.10.3-dev.1 delivery

> For agentic workers: use superpowers:subagent-driven-development or executing-plans task by task. User's incremental CI gates take precedence: finish the current checkpoint before starting the next product-code batch. Artwork generation and review may continue while waiting.

**Goal:** Ship one complete offline companion with the app, download other official companions on demand using the app updater's GitHub routing, and preserve all character data through application and resource updates.

**Architecture:** Stable character IDs own persona/history/wardrobe state. Immutable versioned packages own default artwork and all released holiday outfits. A shared download/install coordinator validates official release metadata and packages before atomically publishing a version directory. WPF and Avalonia provide the same lifecycle with platform-appropriate presentation.

**Stack:** Existing C# shared core, WPF/.NET Framework, Avalonia/.NET 10, GitHub Release assets, PNG/JSON data-only ZIPs, current atomic storage and update download routing.

## Binding decisions and user changes

- Final application version: 0.10.3-dev.1; follow repository tag naming convention.
- Keep Ebao as the complete offline fallback: it has the smallest measured complete artwork set. Keep small catalog avatars/names for all official characters.
- Each official full package contains the default outfit, every released holiday outfit, original animation timing and variants, gaze, preview and character metadata. Preserve full RGBA quality. Palette samples remain review artifacts and are not adopted.
- Select the newest package compatible with the current application, not an unchecked mutable branch or arbitrary latest app asset.
- Reuse existing UpdateUseMirror/UpdateMirrorAddress and UpdateUseProxy/UpdateProxyAddress. Preserve mirror-to-direct fallback and digest checks. Extract shared transport; do not broaden Windows application asset acceptance.
- Right-click selection of a missing character shows a passive non-focus-stealing pet notice: "正在下载角色包，请稍后…". Settings/main UI selection shows a progress dialog with cancellation/retry. Installation runs without a separate import wizard.
- Keep the previous character until validation/install succeeds. A completed superseded request may cache its package but cannot activate over the user's latest choice. Coalesce duplicate downloads.
- Already-installed compatible packages work offline. Metadata/update failures preserve current installed artwork and all preferences.
- After an application update, automatically check/download the complete package for the user's previously selected non-bundled official character. A usable local version remains visible while updating. If missing, temporarily show Ebao without losing the requested ID, then restore it after successful installation. Do not repeatedly reinstall an identical digest.
- First-time onboarding cannot really switch companions: attempts to choose another character only explain that a full package must be downloaded after onboarding. No download, persona switch, pending automatic selection, or saved SkinId change originates from that demonstration.
- Add a matching "全量下载／更新" button immediately after "导入／更新角色包". One action checks all official characters, skips identical installed versions, downloads missing/updated full packages through the same coordinator, and silently imports them. Show aggregate per-character progress, allow cancellation/retry, reuse active downloads, preserve completed installations and the current character. Keep companion/wardrobe selectors equal-width; move action buttons to their own row when narrow rather than crushing the dropdowns. During first onboarding this action only explains availability after the guide, without starting downloads.
- Official registration comes from verified distribution metadata and package identity. User imports cannot claim an official identity by using the same ID. Installing files and earning holiday ownership remain separate.
- User authorized final PR to dev, version tag, and cleanup of superseded temporary PRs/branches. Preserve unrelated branches/worktrees/user files. No changes to main or other version channels.

## Sequential checkpoints

- [x] Wardrobe UI checkpoint PR21 exact head 6ccc8d683f4bd7ec2959677b62aa61672ab1d455 passed Windows39787690615 and Intel/ARM macOS39787690604, including updater/installer and real native macOS UI.
- [x] Image UI checkpoint: PR22 exact head45b2e9ec54b49ab7cde1bb43f1d8cc0d0d160275 passed Windows39809827613 and macOS39809827635 (Intel/ARM), including full UI, updater archive, native bundled launch and installer. Local WPF253/focused126 and full Desktop2494 checks passed; narrow-scroll and three-DPI arrow fixes independently reviewed.
- [ ] Distribution core checkpoint: bounded official catalog/version selection, shared GitHub asset routing, digest validation, atomic install, concurrent request ownership and cancellation. Loopback HTTP fixtures prove exact bytes, wrong digest, route fallback, interrupted download, incompatible version, duplicate request and stale completion cases. Push and wait green.
- [ ] Platform/package integration checkpoint: build complete versioned role ZIPs, embed Ebao only in production, add official placeholders/installed registrations, package-relative holiday sources, startup selected-role refresh and selection UI; onboarding notice-only behavior. Exercise both platforms and real thin production build, then push/wait green.
- [x] Final artwork QA: all five full action matrices, both variants, left/right direction, sixteen gaze directions, eight-frame nonlooping eggs, idle/egg proportion continuity, complete borders and transparent effects. Remaining four independently Approved; root enabled Ready after review. Python structural check5/5, native asset checks12+five outfits, production smoke7401 checks including three complete cache/decode rounds and every character's unlock/persistence lifecycle passed. Full-package integration validation remains required below.
- [ ] Final integration: all required WPF/Avalonia/shared/packaging/installer checks, production source provenance, bounded memory/cache release, complete role package hashes and actual thin runtime size under existing updater limits. Update version/docs/validation report and perform whole-branch review.
- [ ] Create final complete PR to dev, verify final CI, create requested immutable tag at the correct reviewed revision, and close/delete only superseded task PRs/branches after their commits are covered.

## Implementation boundaries

Image WPF owner: src/ImageWorkspacePage.cs, src/ImageGalleryPanel.cs, related WPF UI tests. Image Avalonia owner: desktop/DeskPet.Desktop/ImageWorkspacePage.cs and image presenters/cards, related image UI tests. Root owns shared distribution code, Build.ps1/SDK project/workflow registration, controller integration, package scripts and final delivery. Artwork owner edits only holiday PNGs/definitions and work-only provenance/review material. Do not overwrite another owner's changes.

## Acceptance examples

1. Fresh offline install boots Ebao with its complete default/Halloween actions. Other catalog entries still have a name/avatar. Onboarding selection attempts show explanatory UI only.
2. Selecting Dafeiyu from the pet menu leaves Ebao active and shows a bubble. Download follows the user's updater route, verifies the official digest, validates default+holiday files, installs atomically and then switches persona/history/appearance together.
3. Selecting Marfu while Dafeiyu downloads prevents the earlier task from activating Dafeiyu afterward. Cancel/retry never destroys an installed pack.
4. Updating the app while Gugugaga was selected automatically restores/checks Gugugaga's full compatible package. Offline startup keeps an existing usable package; a missing package falls back temporarily while retaining the user's requested identity.
5. A custom pack using an official ID remains custom. A downloaded official pack updates artwork without resetting persona, voices, conversations or holiday ownership.
6. Adding a later holiday updates the role package independently; users who never choose that character do not download it. Future package limits and metadata are explicitly versioned.
7. Full download/update makes every official companion available offline, retains the current selection, coalesces a concurrent single-character download, skips unchanged digests, and reports individual failures while keeping successful packages.
