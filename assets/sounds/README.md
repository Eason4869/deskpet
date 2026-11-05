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
