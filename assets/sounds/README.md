# Notification sounds

Four original, synthesized marimba cues approved through local audio previews.

| File | Event | Length |
| --- | --- | --- |
| task-complete.wav | Completion | 0.68 s |
| review-needed.wav | Review or answer needed | 0.76 s |
| permission-needed.wav | Permission needed | 0.84 s |
| attention-failed.wav | Actionable failure | 0.74 s |

Assets are mono 16-bit PCM at 48 kHz. They were resampled from the approved
44.1 kHz previews without increasing gain; peak levels remain below -14 dBFS.
They contain no third-party recordings. Both application builds embed them
under `Dafeiyu.Sounds.<filename>`; no runtime download or external player is used.

Notification sounds respect the global sound preference, volume, active audio,
and DND. Suppressed events are not replayed after DND ends.

## Companion operation sounds

The same sound preference controls these three original, user-approved cues.
Their independent volume defaults to 30 (0–100); explicit previews work with
that preference off, but still respect DND and voice audio occupancy.

| File | Event | Length | SHA-256 |
| --- | --- | --- | --- |
| companion-appear.wav | Visible launch or explicit show | 0.38 s | d4b228c2563eba803a5f6efa5fd74b19d1637aca250022b7504f17e51247b17a |
| companion-hide.wav | Explicit hide | 0.40 s | 8f7d97ac3f21e46d7544bb9a6cfddd14018b04a1b722a2ac345f8c6d6770f9ee |
| companion-switch.wav | New companion first rendered | 0.43 s | c0b26a39be738d567f0b3fa7fa204d97315389cce91077bdb3615a7be7914c14 |

These files are native 48 kHz mono PCM16, with no third-party recordings.
`python tools/generate_operation_sounds.py --output <directory>` reproduces the
approved bytes and verifies their hashes (verified with Windows Python 3.12).
Operations have a one-second cooldown, no queue, and yield to notifications;
both yield to speech, recording and calls. Hidden selections and background
updates do not replay a switch sound when the companion later becomes visible.
