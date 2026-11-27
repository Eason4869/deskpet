# Changelog

[简体中文](CHANGELOG.md) | [English](CHANGELOG.en.md)

## 1.0.8

- Update notes open on top of other windows and can be moved by dragging the header.
- Companion Home cards rise into view in sequence, taking about 0.35 seconds each. Once the relationship card settles, its ring draws clockwise to the current progress at 306° per second.
- Relationship stages appear from left to right, followed by staggered entrances for points earned today, active days together, and days since meeting. Journey entries grow downward in time with the keepsake cards on the left.
- Journey entries run from oldest to newest. Windows and macOS share the animation timing and show the final state immediately when reduced motion is enabled.
- Includes the theme and companion transitions, window interactions, Work agent, and settings improvements described in the 1.0.8 development previews below.

## 1.0.8-dev.3 · Development preview

- Full Chat on Windows has a minimum size of 520 × 560 logical pixels. Previously saved sizes below that limit are corrected when reopening.
- Dropdowns match the light and dark palettes and rounded corners. Short choices such as image generation concurrency and the operation sound slider line up with the theme transition controls.
- Companion artwork opacity, theme transition volume, and operation sound volume default to 50%. Saved values carry over.

## 1.0.8-dev.2 · Development preview

- Flow, Ripple, Refraction, and Zipper can reverse smoothly from the current frame when you switch back mid-transition. Selecting the active theme keeps the animation going. Follow System also supports reversals.
- Avatar Ripple and Zipper Entrance continue from the current frame when you select another companion. An animation already under way keeps playing while the next companion loads.
- Theme and companion animations keep playing as you scroll, switch conversations, edit or send messages, or resize the window. Revealed areas update immediately.
- The Windows quick chat bubble now has one continuous outline around its rounded corners and pointer, with a more visible border in dark mode.
- Quick Chat on Windows expands from its screen position into Full Chat. Chat windows briefly fade in and out when opened or closed on their own. Full Chat remembers its size and maximized state.
- Adjust companion artwork opacity in Windows appearance settings. It defaults to 30%, with a live preview in the lower-right corner of Settings and Chat. The companion avatar in Settings has a rounded-square backdrop.
- Companion Home cards rise into place on Windows, and the relationship ring draws clockwise to the current progress.
- The Windows usage heatmap reveals cells from bottom left to top right, and analysis bars grow up from the baseline.

## 1.0.8-dev.1 · Development preview

- The Windows Work agent can use `git_push` to push to the project's GitHub `origin`. The approval prompt shows the branch and commit. GitHub CLI, fine-grained PATs, and an optional HTTP proxy are supported.
- The Workspace pane on Windows can pause, resume, edit, and delete a goal. Pause state is saved per conversation. Queued requests and checkpoints cannot resume a goal invalidated by an edit or deletion.
- The GitHub and Gmail cards on Windows have a right-aligned Manage button. Their rounded dialogs adapt to light and dark themes and provide connection status, sign-in options, and account management. Gmail web sign-in is available independently of in-app OAuth configuration.
- Fixed stale requests overriding a newer companion selection and transitions being cut short or skipped when the chat scrolls automatically or settings are being saved.

## [1.0.7](https://github.com/Eason4869/deskpet/releases/tag/v1.0.7) · Production release

### Appearance and settings

- Switch directly between light and dark in the main chat window on Windows and macOS. The slider moves with your selection. Use the separate monitor button to follow the system, including animated transitions when the system appearance changes.
- Choose Flow, Ripple, Refraction, or Zipper in Appearance settings. Random is the default, and you can turn transitions off. Each animation has a synchronized sound, with a real recording for Zipper. Sounds are off by default and have a separate volume control.
- Settings content adapts to the window width, with switches, the animation picker, and volume aligned on the right. Added missing translations in settings, web search, and the setup guide.
- The theme preview frame fits its three cards. Theme, companion, speech, and size sliders use round thumbs and show their values while dragging in either theme.

### Companions

- The main chat window on Windows and macOS can reveal the next companion with an avatar ripple or a zipper effect once the companion is ready. Choose a companion from the avatar menu. The zipper pulls straight down while the opened edges roll outward.
- Choose Off, Avatar ripple, Zipper reveal, or Random in the partner settings. The choice applies immediately, defaults to Avatar ripple, and respects the system's reduced-motion setting.
- Guga's desktop width and height are each 10% smaller at the selected companion size.

### Scheduled tasks

- Create local scheduled tasks from chat or a form on Windows and macOS. The task page opens to six illustrated templates. Return to them from a draft or the details of a saved task.

### Work and connections

- The Windows work summary groups Git changes, plans, goals, and subagents. Completed subagents stay available, with details in the right pane. Approval prompts and questions appear above the composer.
- Closing a Windows side-chat tab stops its pending requests. Reopen it from the owning conversation's work summary to keep its title, messages, and unsent draft.
- In Windows Work conversations, use `+` or `@` to set a goal or start a plan. Confirm to leave plan mode and execute the selected plan.
- On Windows, connect through your existing GitHub CLI login or open device authorization from DeskPet. Fine-grained personal access tokens remain available. With workspace approvals, the agent can create repositories, commit selected files, push branches, and open pull requests. New repositories default to private.
- The Windows Plugins page adds read-only Gmail access. One-click authorization depends on DeskPet's Google OAuth configuration and approval.

Upgrading from 1.0.5 also brings the post-update release notes, work task recovery, and uninstall wizard improvements listed under the 1.0.6 source tag below.

## 1.0.6-dev.6 · Development preview

**Source tag [v1.0.6-dev.6](https://github.com/Eason4869/deskpet/tree/v1.0.6-dev.6).**

- On Windows and macOS, select the sun or moon in the main chat window to switch directly between light and dark with a sliding thumb. Select the adjacent monitor to follow the system theme. Appearance settings keep all three theme previews. Window transitions default to Random, with Off, Flow, Ripple, Refraction, and Zipper also available.
- Each theme animation has its own synchronized sound. Zipper uses a recording of a real zipper. Sounds are off by default and have a separate volume control.
- Settings pages use narrower, responsive content beneath the existing headings and tabs. Switches align to the right. The animation and volume controls line up with the language selector.
- Localizes text that appeared in English in settings, web search, and the setup guide.
- Scheduled tasks opens to six colorful template tiles by default. Drafts and saved task details both have a Back button, and the sidebar selection has rounded corners.

## 1.0.6-dev.5 · Development preview

- Windows and macOS add scheduled tasks. Approval and question prompts now align above the composer.
- The Windows workspace summary scrolls over its content, hides empty sections, and shows Git changes, plans, and goals separately. Completed subagents stay listed and open in the right-hand pane.
- In Windows Work mode, use `+` or `@` to enable per-conversation goal or plan mode. Once a plan is ready, confirm to exit plan mode and execute the approved plan.
- The Windows Plugins page adds GitHub and Gmail integrations with app icons. The workspace supports Git commits, pushes, and pull requests. Gmail uses read-only access. One-click authorization requires the official DeskPet Google OAuth configuration and Google verification.

## 1.0.6 · Production release

**Available as the [v1.0.6 source tag](https://github.com/Eason4869/deskpet/tree/v1.0.6). The next packaged stable release is 1.0.7.**

- A scrollable release notes dialog opens the first time DeskPet starts after an upgrade. Stable and development preview builds show their own release notes. When the previous version is known, the dialog includes the relevant versions skipped during the upgrade. Chinese and English notes are bundled for offline reading.
- The Windows work summary shows net file changes, side chats for the current conversation, subagent status and sources. Open a diff, switch or create a side chat, browse sent attachments and reply links, or add an attachment from the summary.
- After the first completed side-chat reply, the configured chat model generates a title for the tab and work summary.
- The work summary detects the GitHub repository and current branch from the project's Git checkout. Specify a repository for the project and open it from the summary.
- The Windows work agent stores execution logs per task and migrates older logs automatically. It tracks completion, failure, cancellation and restart interruption separately, preserves confirmed results when a task resumes, and prompts for review when an outcome is uncertain.
- The work task panel shows model request counts and recorded token usage, with controls to stop or resume a task.
- Work agent reply fragments, tool progress and result summaries are saved as encrypted events. Reopening an interrupted conversation restores those details and marks the turn as incomplete in Windows and macOS. Replay makes no model or tool calls.
- The Windows uninstaller shows progress and a dedicated completion page. It keeps application data by default and offers to remove settings, chat history and remaining files from the current installation after uninstalling.

## [1.0.6-dev.3](https://github.com/Eason4869/deskpet/releases/tag/v1.0.6-dev.3) · Development preview

- The Windows uninstall wizard uses the installer’s branding and companion artwork, with a progress view and a dedicated completion page.
- The completion page keeps application data by default and lets you remove settings, chat history, and app files stored in the current installation.

## 1.0.6-dev.2 · Development preview

**Updated on 2026-11-24 (Asia/Shanghai)**

- The work agent saves visible reply fragments, tool progress, and result summaries as ordered, encrypted events.
- Reopening an interrupted work conversation restores saved content and marks it as incomplete in the WPF and Avalonia chat views. Local replay makes zero model or tool calls.

## 1.0.6-dev.1 · Development preview

- The Windows work agent stores execution records by task. Existing journals migrate automatically, completed records no longer block new tasks, and uncertain outcomes prompt for review.
- Work sessions keep distinct outcomes for completion, failure, cancellation, and interruption. Resuming a task preserves confirmed results.
- The work task panel shows model request counts and recorded token usage, with controls to stop or continue a task.

## [1.0.5](https://github.com/Eason4869/deskpet/releases/tag/v1.0.5) · Production release

- Read selected ranges of text, CSV/TSV, XLS/XLSX, DOC/DOCX, PDF, and PPTX in chat. Spreadsheet views show raw values, display formats, formulas, and saved results. Legacy DOC and XLS files open without Office.
- Browse and read ZIP/TAR.GZ entries or run read-only queries against standalone SQLite files. Export a consistent standalone copy of active WAL databases before opening them.
- Inspect MP3, WAV, and MP4 metadata and extract MP4 frames. Text recognition in images and scanned PDFs depends on system OCR and installed languages.
- Preview and send from the same private attachment snapshot. Export original files from sent messages. Model requests contain the selected content and its attachment identifier.
- Remove individual text models from their rows in Windows model configuration, then save the changes.
- Improve Windows installer startup, progress animation, and reinstalls. Installed companions switch using local resources first. Upgrades restore the selected companion and check its assets in the background. Ebao’s regular actions work offline.
- Use one sound toggle for task notifications and companion appear, switch, and hide cues, with a separate volume control for companion sounds.

## [1.0.5-dev.1](https://github.com/Eason4869/deskpet/releases/tag/v1.0.5-dev.1) · Development preview

- Read selected ranges of text, CSV/TSV, XLS/XLSX, DOC/DOCX, PDF, and PPTX attachments. Fixed duplicate final rows in XLSX. Spreadsheet reads distinguish values, display formats, formulas, and saved results. Legacy DOC and XLS can be read without Office.
- Browse ZIP and TAR.GZ entries and run read-only queries against standalone SQLite files. Export a consistent standalone copy before opening an active WAL database.
- Inspect MP3, WAV, and MP4 metadata and extract MP4 frames. Image and scanned PDF OCR depend on available system languages and recognition capabilities. The Windows decoder returns the frame nearest the requested time.
- Private attachment snapshots support range preview, model submission, and original-file export. Requests contain the selected content and an attachment identifier.

## [1.0.4-dev.2](https://github.com/Eason4869/deskpet/releases/tag/v1.0.4-dev.2) · Development preview

- Store Windows installer artwork in separate compression blocks to avoid repeatedly decompressing the application payload before showing the wizard. Preserve the existing UI, five character illustrations, manual selection, and automatic carousel.
- Animate the active installation ring while pending steps remain static; stop updating the ring on the completion page.

## [1.0.4-dev.1](https://github.com/Eason4869/deskpet/releases/tag/v1.0.4-dev.1) · Development preview

- The Windows installer uses a light two-column layout and five companion portraits. Reinstalls keep an editable location with browse and reset actions. Desktop and login-startup choices apply on Finish while preserving custom entries and silent-update behavior.
- Switch installed companions from local resources first. A passive bubble reports installation or repair, supports cancellation and retry, and keeps the current companion while work is busy.
- Restore the last selected companion after an app upgrade, then silently check only that companion for compatible material updates. All current regular Ebao actions are included for offline use. Selecting Ebao silently prepares remaining holiday outfits; offline failure keeps it usable. Existing complete sources and owned outfits retain their whole generation until a complete replacement can safely switch.
- Load and reuse companion images on demand, verify material in the background, and retire unused old packages while protecting active readers and the latest usable version.
- One sound toggle controls task notifications and companion appear, switch and hide cues. Companion cues retain their own volume and three previews, respecting quiet mode and occupied speech audio.
- This is a development preview. Production release 1.0.3 remains Latest.

## [1.0.3](https://github.com/Eason4869/deskpet/releases/tag/v1.0.3) · Production release

- Release the key visibility controls and Windows image-card layout improvements from 1.0.3-dev.1.
- Mask credentials when switching between macOS providers that share the same key.
- Restore accessible names for masked and revealed Windows web-search key fields, including live language changes.
- Add regression coverage for shared-key provider switches and Windows accessible names.

## [1.0.3-dev.1](https://github.com/Eason4869/deskpet/releases/tag/v1.0.3-dev.1) · Development preview

**Release date: 2026-11-06 (Asia/Shanghai)**

- Build on production release 1.0.2 with eye buttons for masked model, voice, image, web-search, messaging and MCP credentials. Keys start hidden, remain editable while revealed, and return to a hidden state when switching providers or leaving a page. Existing save behavior is retained.
- Fix Windows web-search API keys losing their input border when revealed. The border remains after focus moves elsewhere, with theme colors and busy-state disabling preserved.
- Reorganize Windows image-test and conversation image-card actions: a full-width primary Preview button, equal-width Save as and Copy image buttons, a separate folder action, and a divider before image details. Long labels wrap inside narrow cards.
- Add show/hide tooltips and accessible names in all thirteen languages. Update both READMEs with preview downloads, current production links and usage instructions.
- Publish on the Preview channel without replacing production release 1.0.2 as Latest. App, installer and update packages retain the full `1.0.3-dev.1` identity.

## [1.0.2](https://github.com/Eason4869/deskpet/releases/tag/v1.0.2) · Production release

- Include the web search, settings layout, guidance and conversation link-opening improvements from dev.1 and dev.2.
- Align in-app dialogs with a borderless rounded style and compact cache maintenance layout. Center review dialogs over the actual operation window, use the real review window for the guide exercise, and keep actions accessible in small windows.
- Replace tray reminders with consistent passive cards. Add four gentle marimba cues for completion, review, permission and failure, with a separate preference, volume control and immediate DND cancellation without replay afterward.
- Rename the proactive-greeting option and add automatic reading. When global read-aloud is enabled, preset, AI and automatic screen-aware greetings can speak while yielding to other speech, recording and DND.

## 1.0.2-dev.2 · Development preview

- Fix Windows web-source citations at the bottom of long replies jumping to the top instead of opening the page, while preserving automatic scrolling when extending a text selection to the document end.
- Open conversation web links in embedded browser tool tabs by default, including named links, bare URLs, quotes, tables and source lists. Quick chat opens the browser tools in the full chat window; temporary side chat uses its owning window's tools.
- Retain right-click actions for the external browser and copying link addresses, along with image previews. Narrow windows keep the existing full-tab layout.
- Update both READMEs with the source version, web search instructions and link-opening behavior.

## 1.0.2-dev.1 · Development preview

- Fix duplicate `@` and `/` menus when quick chat expands into a full conversation.
- Add native web search and ten external search providers across conversation, quick chat and work mode, plus one custom endpoint with a compatible API format and separate encrypted credentials.
- Reorganize settings while retaining primary navigation, remove the duplicate skills entry, align disclosure controls, and show instruction and capability details directly.
- Shorten setup guidance, correct moved targets and plugin navigation, and preserve progress and verification evidence.

## [1.0.1](https://github.com/Eason4869/deskpet/releases/tag/v1.0.1) · Production release

- Allow custom work and chat sections to be deleted, moving their contents to the default section while preserving records and workspace files. Default sections are retained.
- Use consistent in-app confirmation and error dialogs for section and project deletion, with Cancel focused by default and support for themes and live language changes.
- Roll back section or project membership when deletion cannot be saved, preserving existing data.

## [1.0.0](https://github.com/Eason4869/deskpet/releases/tag/v1.0.0) · First production release

**Release date: 2026-11-03 (Asia/Shanghai)**

DeskPet enters its 1.x production release line. This version brings companion interactions, personal conversations, project work, images, voice and messaging channels into one application, with changes to reply text, conversation-owned tools and temporary side chat. Windows has production installer and portable packages. macOS continues as a platform preview with a separate interface.

### Five companions and everyday interactions

- Ebao, Dafeiyu, Marfu, Xiaobei and Guga retain separate ordinary-chat history, personality, voice preferences and relationship records.
- Drag, pet, feed, nap, use signature actions, wander and follow the pointer. Basic interactions require no model service and remain available while working or waiting for replies.
- Complete official packs include default artwork and ten holiday outfits. Wardrobes, relationship growth, holiday albums and custom ZIP packs are supported. Ebao supports offline startup; other companions download on demand. Material versions are independent of application versions.

### Conversations and project work

- Quick chat, ordinary chat and project work manage separate context, with sections, ordering, pinning, archiving, draft recovery and reading-position retention.
- Windows includes a native file Agent for reading, searching, creating and editing files, with native tool calling, execution records, net file changes, backup restoration, tasks and read-only subtasks.
- Markdown input, image/file attachments, queues, steering, model capabilities and context settings support ongoing work.
- Plan mode, three permission levels, independent review and Windows AppContainer terminal isolation control work access. The Agent terminal is off by default. Ask for approval / Approve for me still require individual human approval for commands. MCP and GitHub writes retain separate confirmation.

### Conversation and tool improvements in this release

- **Preserved public progress:** explanations addressed to the user stay in the reply body. Later tool calls no longer move displayed text into the thinking section, so final results can be read alongside preceding progress.
- **Correct empty-composer height:** adding an image no longer expands an empty editor. Typing or clearing text keeps its height stable. The background character sits above the composer, preserving input and reading space.
- **Improved Open with:** file menus list applicable Windows applications and offer the system application picker.
- **Dedicated messaging icon:** the settings entry is distinct from other features, making platform connections easier to find.
- **Tools retained per conversation:** right-side tabs, page content, selection, hidden state and layout belong to their main conversation and restore when switching within the current window. Closing a tab releases its tool; closing the window releases its instances. Persistence across application restarts is not promised.
- **Side chat inherits main context:** each request reads the owning main conversation's public history alongside its independent temporary messages. Switching to another main conversation does not change ownership. When a valid project is bound and the model supports tools, side chat may inspect that project through read-only tools, without file edits or command execution.
- **Side-chat entry and cancellation:** open from the main composer's `/` menu, window menu or tool tabs. Model/project settings are captured on opening. Its separate history is not saved, and closing its tab cancels its own request.

### Images, voice, messaging and extensions

- Image services, reference-based generation, gallery and task center offer progress, cancellation, explicit retry, favorites, filtering, batch original export and a recycle bin. Agent replies can return images directly.
- Dictation, Windows system speech, cloud speech and Gemini Live use independently configured services.
- Greetings can use local presets or AI. Optional desktop observation requires confirmation of capture scope and receiving services; Windows supports application or full-desktop capture. Observation is off by default, without guaranteed automatic sensitive-content redaction.
- QQ, WeChat, Feishu, DingTalk and WeCom use local outbound connections, separate remote text context and optional access restrictions. Remote users receive no Agent tools.
- Skills support repository discovery and ZIP/local import; MCP has connection management. Text usage distinguishes actual, estimated, unknown and zero values and does not replace provider billing.
- Light, dark and system themes, plus thirteen interface languages, switch immediately after settings are saved.

### Documentation and updates

- Chinese and English READMEs are rewritten with fresh screenshots from this version's isolated preview. They show all five companions, new conversations, project work, temporary side chat, model settings and messaging. All screenshot content is demonstration data, without personal chats or real model names.
- Platform requirements, quick start, model costs, work permissions, local data, configuration, development and license references are covered. Windows capabilities do not imply Mac parity.
- The current changelog starts with the first production release. Complete earlier records move to detailed development logs, retaining original versions, dates, release states and historical notes.
- Windows updates still verify assets and wait for tasks, drafts and data to be safely saved. Existing `data` is retained; downgrades are unsupported. Exit the app and back up the full data directory before migration or upgrade.

### Current limits

- Model, voice and image calls use the user's own services and credentials. Multi-turn work and auxiliary features may incur additional charges. Model visibility does not establish compatibility or account access.
- Messaging currently supports text. QR generation and automated UI checks do not establish real-account authorization or successful inbound/outbound messaging.
- Windows UI changes do not establish Mac parity. macOS previews are ad-hoc signed without Developer ID signing or notarization. Real Mac audio, screen permissions and multi-display behavior still require hardware acceptance testing.
- Windows packages are currently not code-signed. Generated translations have resource checks but not native-speaker review.
- The embedded browser has no download support. MCP HTTP currently supports a JSON subset, not SSE. Temporary side-chat history is not saved; conversation tool states restore only within the current window.

Downloads and usage: [1.0.0 release](https://github.com/Eason4869/deskpet/releases/tag/v1.0.0) · [README](README.en.md) · [installation and backup](docs/installation.md) · [image generation guide](docs/image-generation.md).

---

**Earlier development records:** [English development log](docs/development-log.en.md) · [中文开发日志](docs/development-log.md). Older stable/preview labels and source tags retain their original historical meaning and are not the current download entry.
