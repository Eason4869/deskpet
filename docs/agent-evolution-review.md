# Agent evolution review and validation notes

Local work only. No push, merge to a remote branch, release, deployment, installed-preview replacement, or real-account write is part of this delivery.

## Baseline

Started from main `dbe033140ab20b2e0016f24b7d5d1496e271ac9e`. During validation, main advanced to `8c7c1f9a5d19fe02daefad29d8d17fb1151378d1`. The isolated branch was fast-forwarded; the added files concern Mac release promotion, with no product-source overlap. `PromoteReleaseTests.py` uses mocks for all API and upload subprocesses; its printed “Uploaded” messages represent fixture calls, not uploads.

## Independent review resolutions

An independent read-only review requested changes. The following findings were addressed before guide updates:

| Finding | Resolution | Evidence |
| --- | --- | --- |
| Worker interrupted before its pool callback could still start | Register cancellation before queuing; cancellation retains the scheduler slot until the callback exits; guard terminal state | Deterministic single-thread-pool regression, failed before fix |
| Worker could bypass parent read denial | Freeze and copy parent rules; intersect with read-only tools and no recursion | Actual denied `read_file` in an independent worker, failed before fix |
| Output reserve disagreed with Anthropic wire limit | Derive native and plain request limits from the same context policy | Parsed HTTP request bodies at unknown, 8192 and 32768 capacities |
| Plain conversation lacked total request-byte guard | Shared body builder, byte predicate during context selection, final guard before HTTP | Two individually legal 4 MiB images, streaming/nonstreaming, HTTP never called |
| Unassigned conversations lost trusted instructions and skills | Copy all new global fields to the effective unassigned/quick project | Effective-field mapping regression |
| Non-paragraph Markdown tails lost editable newlines | Preserve LF/CRLF tails outside code/list/quote; ordinary paragraph with source mapping | Structural draft tests; native editor round trips |
| MCP approval showed only a hash | Bind readable server name, endpoint and original operation to the invocation resource | Connected HTTP fixture approval assertion |
| Worker usage ignored tool rounds and parent totals | Record each native request, summary estimates and cumulative worker budget; aggregate to parent separately from current context | Shared native-loop tests and task accounting |

Minor findings: removed the unsupported general worker profile from tool documentation; added busy-pet and review regression suites to Windows CI.

## Additional regression found during validation

A real task-checkpoint save failed in nested conversation folders: appending a GUID to the full hashed target filename exceeded legacy Windows path limits. Use a short sibling temporary filename for checkpoint and session writes, preserving atomic replacement. A deep-directory test failed before this change and passes afterward.

Legacy test fixtures were updated for complete-history retention, actual workspace sessions, queue-start synchronization, current natural animation timing and independently drawn clips. Product assertions continue to check actual displayed frames, HTTP requests, stop state and persisted data.

## Explicit delivery limits

- The user selected current-account **confirm each command** execution. Filesystem and network access are not isolated. `Sandboxed` is unavailable and cannot fall back to unrestricted execution.
- Real model, GitHub-write, microphone and external MCP-account acceptance are not claimed. Local HTTP/model fixtures, owned stdio processes and actual native UI input are identified separately in test logs.
- Windows is the implementation and native validation platform. Shared .NET and Avalonia headless tests on Windows do not establish macOS runtime acceptance.
- Manual project facts are explicit user edits; there is no automatic long-term memory or automatic skill installation.
- MCP supports the stated stdio and HTTP JSON subset; SSE is explicitly unsupported. Connections start only after the user's connect action.
