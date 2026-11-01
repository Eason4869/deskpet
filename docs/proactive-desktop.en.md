# Desktop observation and image descriptions

[简体中文](proactive-desktop.md) | [English](proactive-desktop.en.md)

Applies to the **0.11.4-dev.1 development source preview (no Release)**. These features are outside the 0.11.3 stable downloads. This preview has no separate installable assets and a source version alone does not become an automatic update. Windows and macOS 14+ are in scope; device validation limits appear below.

## Before enabling

Screen-aware proactive speech is **off by default, including after upgrades**. When the existing greeting schedule becomes due, it may capture one image of the **current foreground application window** and let the model speak or skip. It does not capture a whole monitor, inspect other applications or continuously record the screen. A failed window capture does not widen the capture scope.

The image is sent to your configured model service and can incur charges. Window contents may include private chats, documents, passwords or payment details. **Automatic detection or redaction of sensitive content is not guaranteed.** Configure excluded applications and pause observation before handling sensitive content. Exclusions use application identity, rather than just a changing window title.

Observation and replies use ordinary chat permissions and grant no Agent, file, terminal, MCP or desktop-control access. Instructions inside images or descriptions do not authorize actions. One frame cannot reliably establish how long you have been busy, whether you completed a task or how you feel.

## Configure models and observation

1. Configure the dialogue model in the existing language-model settings and confirm its image capability. Automatic observation requires an explicitly image-supported (`Supported`) vision route. An unconfirmed (`Unknown`) capability is not support; use the existing model capability settings to confirm it.
2. Optionally select an **image-description model** from the existing services and models. No separate vision service or copied credentials are needed. Blank means **do not use**.
3. Enable ordinary proactive greetings and AI greetings, then enable **proactive speech based on the current foreground window** in proactive-speech settings. Confirm the scope and actual receiving service addresses.
4. Check the status for direct images, description fallback or unavailable, the receiving services, last observation and safe error category. Pause observation or exclude applications as needed.

An image-capable dialogue model receives the screenshot directly, decides whether to speak and generates the message in one call. The fallback is used only when the dialogue model lacks image support or its capability is unconfirmed, and the selected description model explicitly supports images: the description service receives the image, and the dialogue service receives text. **Review both receiving services and their addresses when confirming consent.** Selecting a fallback does not change the dialogue model or force an image-capable model through description first.

Model or endpoint changes invalidate old observations. A change to the receiving destination pauses automatic observation until you confirm the new destination in settings. Missing authorization, a deleted model or an unavailable route does not silently select another service. Ordinary chat retains the draft and images when no valid route is available.

The conversation model menu also offers **follow global / do not use / specific model** for image descriptions. This fallback handles ordinary chat images and application snapshots, takes effect on the next request and does not override a direct multimodal route. Automatic proactive speech uses the global models; its reply conversation initially inherits the effective choice for that event.

## Local preview, uploaded test and Mac permission

- **Local preview only** displays the screenshot without calling a service; its button shows a countdown before switching to the actual target window.
- **Upload test once** is available only when observation, ordinary greetings and AI greetings are enabled, a valid receiving route is confirmed, and no chat task is active. It captures and calls the service, incurring usage; fallback can make two calls. It does not use the automatic allowance and has its own ten-second repeat-click guard.
- Settings itself occupies the foreground. Follow the preview or upload-test countdown to switch to the actual target window. The ordinary greeting preview does not silently upload screenshots.
- On **macOS 14+**, use the permission button to request system screen-recording access. Background observation does not open permission prompts. Follow the status after denial or revocation; manually quit and reopen the app if required. Ordinary chat and greetings without screen input remain available.

## Timing and pause conditions

The existing default greeting schedule remains **30–60 minutes**. Automatic image analysis is at least **10 minutes** apart and limited to **four attempts in a rolling hour**, with one image per attempt. Issued requests count even when they fail or time out. Capture happens only when due; switching windows does not trigger extra captures.

Quiet mode, lock, sleep, fullscreen, hidden or paused state, chat/voice activity, an active draft panel, onboarding, update countdowns and maintenance block observation. Resuming starts a fresh wait without a backlog. The target is rechecked before capture, before upload and after generation; changes to the window, companion or relevant settings discard stale results.

The same target and thumbnail digest are not uploaded again within 30 minutes; this does not detect every semantically identical view. Skip, cancellation, privacy gates, missing consent or a changed foreground end the attempt quietly. Ordinary capture/network failures may produce a local greeting without screen claims. They do not recapture every five seconds; repeated failures enter a cooldown. If the model receiving images explicitly rejects image input, that automatic observation route pauses until you explicitly refresh its model list or change the relevant model/image-capability configuration in language model settings. Saving unrelated settings does not resume that route; ordinary network errors still recover under the cooldown rules.

## Images and replies

Automatic images are limited to a **1280-pixel longest edge**, **one million pixels** and **2 MiB PNG**. Encoded originals remain only in memory, capped at **two images / 4 MiB combined / 15 minutes**. This excludes transient capture buffers and transport copies. Disabling observation, switching companions, locking, maintenance or exit clears temporary content.

Automatic screenshots and their temporary descriptions are excluded from disk, history attachments, pending-message queues, Agent state, logs and exports. Description caches are bounded, scoped to conversation memory and cleared on conversation close, data clearing or exit. **Manually added images and attachments retain ordinary chat's existing storage rules.** The receiving service's policy governs data already sent to it.

The proactive bubble does not take focus and offers reply and close. A **reply to the last message** entry remains available for 15 minutes. Replying creates or reuses an independent ordinary conversation bound to the original event and message, preserving other drafts, chats, projects and Agent sessions. Repeated clicks do not duplicate the opening message.

Unaccepted bubbles remain in memory; opening and closing one without replying creates no conversation, history, queue or draft artifacts. Replying creates or reuses the same proactive conversation. When history saving is enabled, it retains the opening assistant message, your reply and later answers. The companion menu's **Proactive conversations** entry lists accepted conversations for the current companion, 20 conversations per page; after restart, reopening a record loads its original text and actual replies without a new capture or model request. WPF also exposes these conversations in the ordinary chat sidebar. The proactive opening cannot be regenerated; subsequent ordinary answers can. While the temporary image remains valid, replying may send the original image again with its capture time, **without a new capture**; later manual replies may incur further usage. After the image or description expires, the conversation can continue from text without claiming access to the original image.

## Validation limits

Source checks and automated tests do not establish real-device behavior. Mac SDK builds and runtime results depend on CI for the corresponding commit. **Real Mac capture, permission denial/revocation/reopening and physical multi-monitor behavior remain unvalidated.** Human relevance, interruption and appropriateness evaluation also remains open; no 90% appropriateness or two-person evaluation result is claimed.

Evaluate this preview with synthetic, non-sensitive window content. Keep observation disabled or paused while resolving unsupported-window, permission or model-capability status.
