# Changelog

[简体中文](CHANGELOG.md) | [English](CHANGELOG.en.md)

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
- **Correct empty-composer height:** companion artwork no longer increases an empty editor's height. The background character sits above the composer, preserving input and reading space.
- **Improved Open with:** file menus list applicable Windows applications and offer the system application picker.
- **Dedicated messaging icon:** the settings entry is distinct from other features, making platform connections easier to find.
- **Tools retained per conversation:** right-side tabs, page content, selection, hidden state and layout belong to their main conversation and restore when switching within the current window. Closing a tab releases its tool; closing the window releases its instances. Persistence across application restarts is not promised.
- **Side chat inherits main context:** each request reads the owning main conversation's public history alongside its independent temporary messages. Switching to another main conversation does not change ownership. When a valid project is bound and the model supports tools, side chat may inspect that project through read-only tools, without file edits or command execution.
- **Side-chat entry and cancellation:** open from the main composer, menu or tool tabs. Model/project settings are captured on opening. Its separate history is not saved, and closing its tab cancels its own request.

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

Downloads and usage: [1.0.0 release](https://github.com/Eason4869/deskpet/releases/tag/v1.0.0) · [README](README.en.md) · [installation and backup](docs/installation.md).

---

**Earlier development records:** [English development log](docs/development-log.en.md) · [中文开发日志](docs/development-log.md). Older stable/preview labels and source tags retain their original historical meaning and are not the current download entry.
