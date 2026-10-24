# Data management

Open **General → Data management** in settings. The native app also keeps its direct **Data management** tab and the existing `storage` navigation route. Saving chat history remains a separate toggle. Disabling that toggle does not erase stored history.

**Clear all chat history** and **Restore default preferences** each open their own owned confirmation. Cancel, Escape and the window close button before confirmation do nothing. Confirmed work checks the shared maintenance admission again. Pending sends, queues, audio, jobs or other admitted activities cause a busy refusal; finish or resolve that work before retrying. During the operation, repeat confirmation and closing are disabled until its result appears. Neither operation requires quitting or restarting the pet.

Chat clearing uses [the shared history maintenance service](history-maintenance.md). It clears companion/project transcripts, model session contexts and completed-task replay contexts. It preserves unsent composer drafts and draft attachments, models and encrypted keys, personas and voices, images, relationships and their settings/receipts, project configuration and source/working files, and explicitly saved project facts. These saved facts are separate from chat context. Unreadable files and unsuccessful storage operations produce a partial result with the actual number of files removed and failures. File counts are not conversation counts.

Embedded-only image/PDF originals are preserved independently before their chat files are removed. **View retained attachments** opens the same retained attachment folder on every app start. It reports an empty state if there are no retained files and an error if folder access or the system folder opener fails. This action does not create an empty archive or retain a hidden copy of chat text. Generated images also remain in the existing gallery.

Preference reset saves an atomic detached settings snapshot through `SettingsStore.RestoreDefaultPreferencesAsync(settings, maintenanceGate)` before publishing any preference change. It does not run model/provider normalization or credential migration. Save failure leaves the previous in-memory preferences intact; runtime application only follows successful storage. Runtime application failures are distinguished from save failures. UI controls refresh only the reset preference scope; unrelated model/persona/work drafts and navigation stay intact.

The store reserves its write semaphore asynchronously through the save/publication handoff. Synchronous direct saves refuse immediately while another write owns it. The actual hosts coalesce automatic saves during maintenance; `SaveExactSnapshotAsync` then saves the latest UI state, including new window coordinates, with durable I/O on a worker. A failed follow-up autosave remains pending and is reported separately: it cannot undo or misreport an already saved preference reset. Direct language/MCP/onboarding settings editors are blocked by the owned modal owner; their ordinary synchronous save contract still reports a busy storage error if invoked concurrently by other callers.

The explicit reset whitelist is:

| Group | `PetSettings` fields restored from a new settings instance |
| --- | --- |
| Appearance and display | `ThemeMode`, `Scale`, `Volume`, `Topmost`, `ImageGalleryLayout`, `ImageGenerationMotionEnabled` |
| Pet behavior and greetings | `FollowCursor`, `Wander`, `StumbleEnabled`, `AutoSleepEnabled`, `GreetingEnabled`, `AiGreetingEnabled`, `GreetingMinMinutes`, `GreetingMaxMinutes` |
| General | `QuietMode`, `HideOnFullscreen`, `ClickThrough`, `LockPosition`, `SaveChatHistory`, `ImageGenerationConcurrency` |
| Chat presentation/input | `ShowPredictedMessages`, `PlainTextEditor`, `ShowContextUsage`, `SendOnEnter`, `FollowUpBehavior` |
| Shortcuts | `Hotkeys`, `LocalShortcuts` |

Everything outside that whitelist is retained. This includes language, window coordinates, current companion, all model endpoints/providers/encrypted credential strings, persona and voice profiles, workspaces/projects/agent safety and execution configuration, update channel and routing settings, onboarding completion, and the separate relationship data/settings. Preference reset does not delete chat or image files. Native hotkey registration continues to use the app's existing platform-specific behavior; real macOS hotkey and OS folder-launch acceptance requires a macOS device.

Focused verification is registered as Framework `DataManagementTests` and `DataManagementUiTests`, portable `--data-management-only`, and native headless `--data-management-ui-only`. Destructive verification uses only GUID fixture directories. Final full-suite, translation provenance, packages and real-device acceptance belong to the later integration stage.
