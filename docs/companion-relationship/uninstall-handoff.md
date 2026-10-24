# Windows uninstall handoff

The Windows maintenance helper supports an uninstall protocol independently of
the existing update protocol. The confirmation UI and reversible close-owner
transaction must integrate this API before exposing it to users.

## Host contract

`await UninstallHandoff.PrepareAsync(installationDirectory)` returns an
acknowledged `UninstallTicket`. It verifies the running host belongs to that
installation and copies the matching helper into
`data/uninstall/<32-hex-id>/`. Preparation does not authorize uninstall or close
the host. It can fail, and the host must remain alive on preparation failure.

The host must first reserve its maintenance gate, reject active work and drain
the actual history, job, relationship, image and settings tails through an
explicit authorized owner. A nested producer admission or a coalesced
`SaveSettings` call is not evidence of persistence. The gate and ticket must
remain owned until actual close, or be released on failure/cancellation. This
helper does not grant any maintenance bypass.

Call `ticket.Commit()` **only from the actual successful `Closed` notification**,
after those drains succeed. Calling `Close()` is not sufficient: another closing
handler can still cancel it. Commit persists a one-byte authorization record
through a flushed temporary file and atomic rename. It means permission to
proceed after process exit, not successful removal.

Before commit, `Abort()` or `Dispose()` prevents removal; an aborted ticket
cannot commit. After commit, disposal releases the process handle without
cancelling the authorized operation. A failed commit throws, attempts to write
failure evidence, and never publishes authorization. An already closed window
cannot be promised to remain alive after this late failure.

## Helper checks

`DeskPet.Update.exe --uninstall <job>` requires a version-1 `UninstallJob` in
`data/uninstall/<id>/uninstall.json`. The job contains installation/work paths,
registered uninstaller path and SHA-256 hashes for its EXE and DAT, parent PID and
UTC start ticks, and installed display/numeric/build identity. The helper:

1. Rejects foreign paths, linked ancestors/files, missing registration, invalid
   build identity or a copied helper whose bytes differ from the installed one.
2. Opens the exact live parent process and checks PID/start time/executable path
   before writing `ready`. A missing initial parent never establishes trust.
3. Waits for that retained process handle to exit, checks durable authorization,
   then waits for the installation mutex **object to disappear**. Each successful
   `OpenExisting` probe is immediately disposed. Access denied fails closed.
4. Revalidates registration, paths, hashes and identities, and runs the registered
   Inno uninstaller with `/SILENT /NORESTART` plus an owned log path. It never
   force-kills processes, requests reboot, or recursively deletes user data.
5. Checks the uninstaller exit code, missing application executable and removed
   registration before recording success.

Inno documents that [silent uninstall suppresses startup confirmation and
NORESTART prevents reboot](https://jrsoftware.org/ishelp/topic_uninstcmdline.htm).
Its [AppMutex check tests named-object existence](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm).
The application creates `Mutex(false)` without owning it, so `WaitOne` success is
not evidence that the app has exited.

Parent and mutex waits each have a two-minute deadline. A late failure leaves a
failure receipt where storage permits; it does not force restart or remove
anything itself. Inno critical error messages remain possible, and another app
starting during its startup check can make Inno refuse removal. No stronger
success claim is made. When all relevant storage is unavailable, durable failure
evidence cannot be guaranteed.

The retained job folder holds `ready`, `commit-stage`, `commit`, `abort`,
`result.json`, optional `error.log`, and `inno-uninstall.log`. A best-effort
`data/uninstall-result.json` mirrors the result. Receipts from abandoned tickets
are failure evidence, not instructions to show an unsolicited startup warning.
No receipts authorize a later retry; each retry requires a fresh confirmed job.

## Owned acceptance fixture

Set `DESKPET_TEST_INNO` to an existing Inno compiler and run
`python tests/UninstallHandoffSmoke.py` on Windows. It creates unique AppId and
installation-mutex values, an inert parent instead of the production
single-instance/wake process, and an inactive desktop without switching the
user's input desktop. Its minimal Inno script has no startup values, shortcuts or
Run actions. Only its unique registration can be modified or removed.

The fixture uses actual production helper method bodies, resources and build
identity with test-only registration/mutex constants. It is not the exact release
candidate installer. It exercises real ticket preparation and cancellation,
trust failures, failed commit publication, parent-death behavior, mutex existence
and access-denied probes, actual Inno removal, and byte-preserved settings,
histories, images, roles, relationships and receipts. Actual WPF confirmation,
cancel/Esc/X and authorized close-owner integration remain a separate acceptance
gate. No macOS app-removal behavior is provided.
