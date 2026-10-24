[简体中文](README.md) | [English](README.en.md)

<div align="center">
  <picture>
    <source media="(max-width: 640px)" srcset="previews/readme-hero-mobile.svg">
    <img src="previews/readme-hero.svg" width="1200" alt="DeskPet: Ebao, Dafeiyu, Marfu and Xiaobei share your desktop, conversations and projects">
  </picture>
  <h1>DeskPet · 桌面伙伴</h1>
  <p>Animated companions · Individual personalities · Project work · Dictation and speech</p>
  <p><a href="#download">Download</a> · <a href="#get-started">Get started</a> · <a href="docs/configuration-guide.md">Setup guide</a> · <a href="CHANGELOG.en.md">Changelog</a></p>
</div>

DeskPet is a companion that lives on your desktop. Let it wander, follow the pointer, enjoy a head pat or have a snack. Connect your own model service to chat, explore ideas and work with project files.

The current preview release is **[0.10.5-dev.3](https://github.com/Eason4869/deskpet/releases/tag/v0.10.5-dev.3)**. The setup guide starts with a language page and a Restart button whose caption follows the selected language. Restart applies the choice and resumes the guide. Before you start gives an estimated 15–25 minutes for the full guide. Choose the blue Start guide action for step-by-step setup, including release channels, or the gray Explore on my own action to go straight to congratulations. Both paths require final confirmation. The guide stays centered without a main window and anchored to its right when one is open. Building on the completed Chinese JSON resource refactor, this branch adds multilingual support and Agent background jobs. The language selector supports thirteen languages and **System default**. Open the avatar menu and choose **Language**, immediately above **Settings**. The saved choice applies on the next launch; manual choices are retained, and unsupported system languages use English. Default personalities and requested reply languages follow the active language, while custom content is preserved.

Languages use their native names: 简体中文, 繁體中文, English, 日本語, 한국어, Français (France), Deutsch (Deutschland), हिन्दी (भारत), Bahasa Indonesia, Italiano (Italia), Português (Brasil), Español (Latinoamérica) and Español (España).

Persistent work conversations on Windows WPF and Avalonia support application-owned background tasks: original-reply task cards, project task lists, filters and sorting, read-only paged logs, stop and retry. Loopback preview addresses are extracted from logs and open in a browser only after an explicit click. Closing a conversation or archiving it does not end its tasks. Active project settings, deletion and exit respect task ownership and cleanup. macOS retains conversation and task-record UI without local process execution. Formal setup covers GitHub routing, stable/preview channels and character selection, followed by an explicit full official-pack download/update recommendation. Downloads continue while later guidance proceeds or closes; usable old artwork remains visible and the last selected role reloads after its update. See the [implementation record](docs/agent-background-jobs/implementation-progress.md) and [resource guide](docs/localization/zh-CN-resource-guide.md). This preview is released from `dev` with installable packages and complete resources; it does not replace the stable latest release. See the [onboarding acceptance record](docs/onboarding/acceptance-v0.10.5-dev.3.md) and choose the preview or stable downloads below.

**Current stable release: [0.10.4](https://github.com/Eason4869/deskpet/releases/tag/v0.10.4).** It includes Windows installer/portable packages, macOS previews for Apple Silicon and Intel, full source, complete HD packs for all five official companions and an authoring template. See [Platforms and updates](#platforms-and-updates) for platform differences. Application screenshots on this page show the Windows interface in Chinese.

Version 0.10.4 fixes companion restoration after restarting: a valid installed pack restores the previously selected companion offline. Features from 0.10.3 remain available: Halloween outfits for five companions, collectible hover surprises and seasonal greetings; a shared image-generation queue, task center, multiple reply images and gallery management; skill repository discovery, a LAN model relay and conversation drafts; dark settings and in-conversation confirmations. Only Ebao's full HD artwork is bundled. Other official companions download on first selection, or can all be downloaded/updated from Settings. See [holiday outfits](docs/holiday-outfits.md), [image generation](docs/image-generation.md), [character packs](docs/character-packs.md) and the [changelog](CHANGELOG.en.md).

Historical development releases and source snapshots are retained in the changelog. The stable channel receives stable versions only; the preview channel accepts newer preview or stable versions. Neither channel downgrades the app.

Choose an update channel in About. This saves a preference and never downgrades the app. **Update and restart now** skips the remaining five-second wait while retaining protections for running tasks and unsaved content. The authoring template is a separate download. Since 0.10.3-dev.1, non-default companions use independent complete HD packs and the same GitHub acceleration settings as application updates. See [release and update naming](docs/release-naming.md).

## Meet your companions

<picture>
  <source media="(max-width: 640px)" srcset="previews/readme-interaction-mobile.svg">
  <img src="previews/readme-interaction.svg" width="1200" alt="Companion artwork, action palette and quick chat; interactions need no AI configuration, while chat uses a configured model">
</picture>

Choose light, dark or system appearance in **Settings → General → Appearance**. Appearance changes save automatically and take effect immediately; this is separate from the language choice, which applies on restart.

Choose any of five official companions or import a custom appearance. A fresh installation starts with **Ebao**. Selecting another official companion downloads and installs its complete HD pack before switching. During formal setup you can select a character directly; missing packages prepare in the background, with an explicit full official-pack download/update action. You can continue or close the guide while preparation runs. Each companion has separate ordinary-chat history, personality and voice preferences.

| Companion | Meet them |
| --- | --- |
| **Ebao (E包)** | A sharp-tongued companion with white hair, blue eyes and blue-and-white sportswear. |
| **Dafeiyu (大肥鱼)** | A gentle, playful whale girl in blue and white. |
| **Marfu (马尔芙)** | A clever, playful magical girl with blonde hair and green eyes. |
| **Xiaobei (小贝)** | A quick-witted, gentle technology-themed boy in black, white and teal. |
| **Guga (咕嘎)** | A curious, food-loving girl in a penguin outfit, with black hair, gray-blue eyes and a little temper. |

Import a [complete character-pack ZIP](docs/character-packs.md) to update an avatar, personality, both animation sets and voice recommendations. Packs with the same ID retain a single listing and personal records. **Download/update all** installs or updates all official complete packs. Unlocked outfits are available in the current companion's Wardrobe, with personal records preserved.

1. **Drag, click and pet:** move your companion and trigger interactions. Locking its position keeps it in place.
2. **Middle-click:** open the action palette for a nap, snack or signature action. Running actions move a short distance and stop at screen edges; a locked companion performs them in place.
3. **Double-click the companion:** open quick chat. **Right-click it**, or **double-click the tray icon**, to open the full conversation window.

Your companion remains interactive while thinking or working. Dragging, pausing or changing an action stops manual running. **Desktop interactions need no AI configuration. Text chat, dictation, speech and Live require the corresponding model services.**

## Chat and work in one window

<picture>
  <source media="(max-width: 640px)" srcset="previews/readme-workspace-mobile.svg">
  <img src="previews/readme-workspace.svg" width="1200" alt="The complete Windows 0.10.0 workspace, with navigation, six sample conversations, chat and the composer">
</picture>

<sub>This Windows 0.10.0 example preserves the complete window. Conversations are isolated local fixtures, contain no personal records and made no model requests. View the [original workspace image](previews/workspace-0.10.0.png), [action palette](previews/action-palette.png) or [quick chat](previews/quick-chat.png).</sub>

### From a message to a project

| Mode | How it works |
| --- | --- |
| **Quick chat** | Double-click your companion for a short conversation. Reopening starts a new topic; previous records remain in that companion's Quick Chat group. |
| **Chat** | Use your companion's personality in a full conversation, with separate context and drafts in the default or a chosen chat section. |
| **Work** | Bind a project directory, read/search/edit files and inspect actual tool records. The text model must support native tool calling. |

Chat sections contain conversations; work sections contain projects and their conversations. Create or rename sections, reorder conversations by dragging, move work conversations to another project or chat conversations to another chat section, and pin, archive or group projects and conversations. Switching pages or conversations preserves drafts and reading position. Collapse the sidebar with the title-bar button or **Ctrl + Shift + S**.

### Write comfortably and read complete replies

The composer supports Markdown headings, lists, quotes, code and tables. Switch to plain text in **Work settings → Editor**. The default shortcuts are **Enter to send** and **Shift + Enter for a new line**.

Use **+**, paste or drag and drop to add image, file and folder references. Windows also supports snapshots of application windows. Enlarge, zoom and pan uploaded or returned images inside the window; press **Esc** or click outside to close. Copy, read aloud, regenerate or export replies.

Dictation turns speech into a draft. Toggle automatic read-aloud above the conversation; use Live for real-time voice. **Message predictions** make additional text-model requests and can be disabled in editor settings. Accepting a suggestion does not send it automatically. Add instructions during a run using **Queue** or **Steer**; steering joins the next adjustable request boundary and does not undo requests already sent or changes already made.

### Tools beside the conversation

The image workspace provides **Generate / Gallery / Tasks**. Configure an independent image model, upload references, browse a waterfall or grid gallery, inspect details, preview or save originals. Agent tools can return images in the conversation, ask the user a question, inspect local images and perform read-only Git operations. See the [image guide](docs/image-generation.md).

The 0.10.2-dev.3 image features are implemented:

- **Generation animation:** pages and conversations share a rounded dot-field animation with actual stages, elapsed time and cancellation state.
- **Gallery management:** favorites, multiple selection, batch export of originals, service/model/source/time filters, and a recycle bin with reference-aware cleanup.
- **Task center:** one queue with concurrency limits, completion notifications, failure reasons, explicit retry and restart recovery.
- **Multiple reply images:** a large preview and selectable thumbnails adapt to narrow windows while preserving selection and generation state.

See [image generation](docs/image-generation.md) for usage, saving, recovery and platform limits.

Use the tool-tab **+** button or **Ctrl + Shift + B** to open a browser, file preview, PowerShell terminal, temporary side chat or file-change view. Chat and tools support split layouts; **Ctrl + Shift + F** switches the full tab view. Layouts adapt to narrower windows.

File cards preview HTML, Markdown and line-numbered source; open them in the system app, reveal their location, copy a path or save a copy. External HTML resources are disabled by default. The embedded browser does not yet support downloads. Temporary side chat does not read the main conversation context or project files and is not saved on close.

The main conversation stays in the first tab. Hiding tools or changing the layout preserves tool state; closing a tab releases its instance. Manual terminals are managed independently of Agent terminal settings and approval. They retain their opening project/sandbox context and terminate their process tree when closed.

## Download

### Preview 0.10.5-dev.3 (dev)

Try multilingual support, background jobs and the updated setup guide, and help report issues. The stable version remains 0.10.4. Complete companion packs, the pack index, standalone executable and template are on [this release page](https://github.com/Eason4869/deskpet/releases/tag/v0.10.5-dev.3). Mac packages remain ad-hoc platform previews without Developer ID signing or notarization.

| Platform or resource | 0.10.5-dev.3 preview download |
| --- | --- |
| Windows installer — recommended | [DeskPet-Windows-Setup-0.10.5-dev.3.exe](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-Windows-Setup-0.10.5-dev.3.exe) |
| Windows portable | [DeskPet-Windows-0.10.5-dev.3.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-Windows-0.10.5-dev.3.zip) |
| Mac Apple Silicon | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-macOS-0.10.5-dev.3-arm64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-macOS-0.10.5-dev.3-arm64-preview.zip) |
| Mac Intel | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-macOS-0.10.5-dev.3-x86_64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-macOS-0.10.5-dev.3-x86_64-preview.zip) |
| Full source | [DeskPet-Source-0.10.5-dev.3.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-Source-0.10.5-dev.3.zip) |
| Authoring template | [DeskPet-Skin-Template-v2-0.10.5-dev.3.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/DeskPet-Skin-Template-v2-0.10.5-dev.3.zip) |
| Checksums | [SHA256.txt](https://github.com/Eason4869/deskpet/releases/download/v0.10.5-dev.3/SHA256.txt) |

### Stable 0.10.4 (main)

Windows requires **Windows 10/11 x64 and .NET Framework 4.8+**. The macOS preview requires **macOS 14+**. Choose the package for your operating system and processor. Release notes and checksums are on the [0.10.4 release page](https://github.com/Eason4869/deskpet/releases/tag/v0.10.4).

| Platform | Download |
| --- | --- |
| **Windows installer — recommended** | [DeskPet-Windows-Setup-0.10.4.exe](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Windows-Setup-0.10.4.exe) |
| Windows portable | [DeskPet-Windows-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Windows-0.10.4.zip) |
| Mac Apple Silicon preview | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-arm64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-arm64-preview.zip) |
| Mac Intel preview | [DMG](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-x86_64-preview.dmg) · [ZIP](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-macOS-0.10.4-x86_64-preview.zip) |

<details>
<summary>Source, companion packs, authoring template and SHA256</summary>

| Asset | Download |
| --- | --- |
| Full source | [DeskPet-Source-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Source-0.10.4.zip) |
| Complete companion packs | [Ebao](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-e-bao-0.10.4.zip) · [Dafeiyu](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-dafeiyu-0.10.4.zip) · [Marfu](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-marfu-0.10.4.zip) · [Xiaobei](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-xiaobei-0.10.4.zip) · [Guga](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companion-gugugaga-0.10.4.zip) |
| Official pack index | [DeskPet-Companions-0.10.4.json](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Companions-0.10.4.json) |
| Character authoring template | [DeskPet-Skin-Template-v2-0.10.4.zip](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/DeskPet-Skin-Template-v2-0.10.4.zip) |
| Asset checksums | [SHA256.txt](https://github.com/Eason4869/deskpet/releases/download/v0.10.4/SHA256.txt) |

</details>

## Get started

1. **Install or extract.** Run the Windows installer, or fully extract the portable ZIP to a writable folder and launch `桌面伙伴.exe`. Do not launch directly inside the archive.
2. **Choose a companion.** Try dragging, petting and the action palette. Skip AI setup if you only want desktop interactions.
3. **Configure models.** Open **Settings → Setup guide** and enter the service URL, protocol, key and model for text, dictation, speech or Live as needed. Test a connection explicitly.
4. **Chat or work.** Double-click for quick chat, or open the full window to manage context, projects and files.

The setup guide supports skipping steps/chapters, continuing later and retaining progress. Test requests, sound and microphone access start only when you explicitly select a test action. The guide appears once on a fresh install or when entering a higher major/minor version series; same-series patches do not repeat it. Reopen it manually in Settings.

Bundled guides can be read offline: [installation, updates and backup](docs/installation.md), [setup and usage](docs/configuration-guide.md), and [model and voice configuration](docs/dialogue-setup.md). Offline documentation does not make remote models available offline. Some detailed guides remain in Chinese.

## You control work permissions

Windows includes a file Agent and does not require a separate Agent CLI. Inspect net file changes, execution output, approvals and subtask records. The text model must support native OpenAI or Anthropic tool calling.

| Conversation permission | Behavior |
| --- | --- |
| **Ask for approval** | Routine project-file operations can proceed. Exceptions such as access outside the workspace or networking require confirmation; Agent terminal commands require individual approval. |
| **Approve for me** | The default for new configurations. An independent reviewer model checks eligible operations; uncertainty or review failure falls back to human confirmation. Agent terminal commands still require individual approval. |
| **Full access** | After confirmation, expands access to files available to the current account and disables the workspace sandbox. It does not grant administrator privileges. |

The Agent terminal is disabled by default. With the workspace sandbox enabled, Windows uses **AppContainer** to isolate the terminal and its children, restrict directory access and block networking by default. An isolation failure stops execution. **Sandboxing does not waive terminal approval in Ask for approval or Approve for me modes.** The host enforces file/web-tool permissions. MCP runs outside the terminal sandbox, and MCP/GitHub writes require their own confirmations.

Plan mode rejects writes at the tool layer. File modifications are backed up by default, and restoration checks later changes. Read-only subtasks inherit their parent's restrictions. Project facts are maintained manually; model conclusions do not automatically become execution evidence. See [Agent workflows](docs/agent-workflows.md) and the [file Agent guide](docs/conversation-agent.md).

## Extensions, usage and personal settings

| Entry | Features |
| --- | --- |
| **Plugins → Skills** | Add a public GitHub repository to search, preview and install skills; ZIP and local-folder imports also work. Load on demand, enable/disable or remove, and retain provenance. Installation does not automatically execute scripts. |
| **Plugins → MCP** | Add stdio or HTTP servers, then connect explicitly. They do not start automatically with the app. HTTP currently supports a JSON subset, not SSE. |
| **Usage** | Request/token counts, a yearly heat map, 7/30-day history and conversation rankings, filtered by provider, model, project and feature. |
| **Settings** | Search companion, model, work and general preferences: size, topmost, position lock, click-through, full-screen hiding, quiet mode and shortcuts. |

Usage covers newly collected text requests for chat, work, subtasks, review, predictions, titles, compaction and greetings. It does not yet include ASR, TTS or Live. Actual, estimated, unknown and true zero values are distinct. The ledger stores no prompts, reply bodies or keys, and is not a provider bill or subscription balance.

Settings, credentials, history and imported content are stored locally. Back up the complete `data` folder before migration or an overwrite upgrade. Files, images and tool results can be sent to the model service you choose; multi-turn work is billed under that provider's rules. For custom companions, see the [appearance format](docs/skin-format.md) and [interaction extensions](docs/interaction-extension.md).

## Platforms and updates

| Platform | Current scope |
| --- | --- |
| **Windows** | Installer and portable packages with the unified workspace, companion interactions, file Agent, tool pages and AppContainer terminal isolation shown here. |
| **macOS preview** | Apple Silicon/Intel builds with a separate UI and partially shared core. Windows workspace, onboarding and file-Agent features do not imply equivalent Mac support; consult the preview release notes. |

Windows offers stable/preview channels in **About and updates**, verifies and installs at a safe idle point, and never downgrades. Older clients must first update to 0.10.0 to discover later public dev releases. A source tag alone is not an update candidate. **Installed versions 0.5.4 and earlier** must first install 0.5.6 manually or install a current version manually; portable ZIPs are unaffected by installer renaming. See ongoing work on the [dev branch](https://github.com/Eason4869/deskpet/tree/dev).

The Windows installer is not currently code-signed. Mac previews are ad-hoc signed and have not been notarized with Apple Developer ID. See [installation and backup](docs/installation.md), [artwork/platform sources](docs/sources.md) and [third-party notices](docs/third-party-notices.md).

## Planned work

- [ ] Application connections: manage authorization, connection state and available capabilities for external apps.
- [ ] Plugin-package runtime: import packages, validate manifests/dependencies and manage activation and lifecycle.

---

Author: **彧晟Eason** · [Issues](https://github.com/Eason4869/deskpet/issues) · [Changelog](CHANGELOG.en.md) · [Setup guide](docs/configuration-guide.md) · [Sources and licenses](docs/sources.md)
