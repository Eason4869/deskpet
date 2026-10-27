# Clearing conversation history

`Dafeiyu.Maintenance.HistoryMaintenanceService` is the shared storage operation for the WPF and native data-management pages. It does not change preferences, start uninstall, or provide a confirmation UI.

Construct it with the application's data directory, its existing `DataMaintenanceGate`, and its existing `ImageGenerationTaskManager` when image services are installed. Call `ClearAllAsync()` only after the separate history confirmation. Do not acquire a maintenance lease around this call: the service owns that non-reentrant lease, reserves admission synchronously, drains existing writers, and performs filesystem work on the thread pool.

The result exposes:

- `Status`: `Complete`, `Busy`, or `Partial`. Busy work or pending queues must be resolved before retry. An unreadable persisted queue also returns `Busy`, with a diagnostic in `Failures`, because its pending user work cannot safely be discarded.
- `RemovedFiles`: the number of files actually removed, including checkpoint/session files and migration backups. This is not a conversation count. Loaded histories with persistence disabled can be cleared with zero files removed.
- `Failures`: paths and diagnostic reasons. A preservation failure prevents the removal phase; a deletion failure can follow earlier successful removals. Never show a complete-success notice for a partial result.
- `AttachmentDirectory`: the stable absolute `preserved-attachments` directory under the data root. Expose an **Open preserved attachments** action when it exists. A clear with no embedded media need not create it.

## Storage boundary

The service scans only these canonical locations, without invoking lazy migrations:

| Location relative to the data root | Treatment |
| --- | --- |
| `history/*.history` | Preserve embedded media, then remove |
| `conversations/<id>/history/*.history` | Preserve embedded media, then remove |
| `projects/<id>/chats/<id>/history/*.history` | Preserve embedded media, then remove |
| `ownership-backups/<conversation>/*.history` | Preserve embedded media, then remove before primary histories |
| Each corresponding `history/agent-tasks/*.task` | Preserve guidance attachments, then remove replay/resume checkpoints |
| Each corresponding `kernel-sessions/*.session` | Strictly inspect supported format/structure, then remove model session context |
| Root/workspace `queued-messages.json` and `queue.json` | Inspect encrypted content; refuse if pending or unreadable; keep empty queue files |
| `generated-images/tasks.dat` and loaded image task managers | Preserve tasks/assets; detach old conversation delivery origins |

Registered sessions supply every cached persona history and pending checkpoint attachment. Under the exclusive lease, the service clears matching successfully removed histories in memory, task retry caches, model pending/delivered/events, context-usage and prediction caches, and old notices. Ordinary close, persona selection and checkpoint retry therefore cannot rewrite successfully cleared transcripts. Old task checkpoints are removed and their cached task identities become nonresumable; clearing does not turn a completed disabled-growth task into a new reward opportunity.

The service first validates and preserves all embedded media, including media present only in loaded memory or a pending checkpoint. Files are content-addressed by SHA-256 with a media extension, flushed durably, reopened, and compared byte-for-byte before the containing history can be removed. Image media are validated with the existing image parser/decoder; PDFs retain their original bytes after signature validation. The archive contains no chat text, prompts, source paths or account metadata. Existing external `SourcePath` files are untouched. A corrupt/unreadable history, malformed media, archive failure, or storage warning keeps the original history and reports a partial result.

Kernel session files are inspected before any archive writes or removals. The read-only inspector supports the current `DPS1` encrypted envelope and legacy plaintext JSON with the current session schema. It rejects unknown members/versions and invalid identities, collections, inputs, events or counters. Corrupt, undecryptable or unsupported unloaded contexts remain byte-for-byte unchanged with a partial result and file diagnostic, matching loaded corruption retention. Inspection never calls the migrating session loader or creates files.

Directories are pinned without walking their subtrees. Removal checks the exact file's content hash and uses the shared pinned-directory deletion primitive. No history directory is recursively deleted. If deletion fails, that file remains and its loaded history remains available. Retry inspects the current state again. Completed removals are not rolled back.

Generated image tasks and gallery metadata (including image prompts) remain available. Their conversation origin identifiers are removed durably before history deletion, including the live manager cache; stale in-process retries cannot restore the old origin. Temporary generated images and task-only reference images are promoted before releasing history references. A partial deletion may already have detached delivery origins, but preserves the gallery assets and task records.

## Retained data

Settings, model credentials/providers, companion/persona definitions, project catalogs and configuration, relationship state/settings/receipts/outbox, unsent `InputDraft` text and composer draft attachments are retained. Explicitly saved `project-facts.bin` is project memory and remains byte-for-byte unchanged. Agent workspace source, file backups, `.deskpet-agent-kernel/jobs`, and project execution/process ownership journals are not traversed or removed.

The gate coordinates this running application, not arbitrary external processes. Path pins and content checks provide additional filesystem protection; another application instance must continue to obey the application's existing single-instance and process-ownership rules.
