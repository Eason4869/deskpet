# Windows installer startup verification: 1.0.4-dev.2

Verified locally on 2026-11-14, Windows 10.0.26300 x64, Inno Setup 6.7.0.

## Change and cause

The previous solid archive placed the runtime before the artwork extracted by
`InitializeWizard`. Extracting the logo and then earlier artwork repeatedly
rewound the solid stream and decompressed the large runtime before any usable
wizard appeared. The baseline log records repeated approximately 20-second gaps
between temporary artwork extractions.

The PNG entries now use `solidbreak nocompression`. The small, already-compressed
UI images can be read directly without traversing the runtime or decompressing
another LZMA stream. Existing artwork bytes, layout, carousel selection logic,
and the 4.5-second automatic carousel interval are unchanged. There is no new
loading dialog.

## Measured results

The timer starts immediately before CreateProcess and ends when the directory
page is visible, exposes its path field and actions, and responds to a timed
window message. These are complete installers, not the small native UI fixture.

| Variant | Completed launches | Ready time |
| --- | ---: | --- |
| Original archive layout | 1 | 217.1951 seconds |
| Separate compressed UI blocks, intermediate experiment | 3 | 1.4932–1.6100 seconds |
| Final directly stored PNGs, final acceptance | 5 | 1.1887–1.4640 seconds; median 1.2091 seconds |

The final five samples are 1.4639775, 1.1886767, 1.2090931, 1.2166003,
and 1.2045627 seconds. A preceding five-launch check of the final layout also
stayed below two seconds (1.2087–1.2770 seconds).

The baseline and final comparison installers use the same 24-file runtime
payload and compiler. Their copied scripts replace only the installation mutex
with the same test mutex and resolve artwork paths, so the user's running pet
does not block startup. They are opened on a private inactive desktop, then
cancelled; the comparison does not install or close the user's application.

Both packages are hashed before measurement, so these observations do not
represent a cold filesystem-cache or post-reboot test. They include startup
work after CreateProcess, but cannot measure Explorer/security checks that
precede process creation. They are evidence for this machine, not a universal
two-second guarantee on every PC. No antivirus or security settings were changed.

## UI and installation checks

- Final native installation suite: 72 checks passed, covering editable/browsable
  paths, previous-location reuse, intermediate progress, completion options,
  shortcut ownership, silent updates, and uninstall behavior.
- All five full page bodies match the baseline screenshots pixel for pixel.
  The settled title bar also matches. Every manual role selection and a complete
  five-role automatic cycle passed.
- Title bars are compared separately from the five page bodies because native
  layered-button capture can vary: the initial strict full-image comparison
  found a 9×8-pixel transient at the close glyph in one baseline capture.
  Capture now requests a synchronous redraw; no artwork or UI code was changed
  to accommodate that capture artifact.
- The first reference-image rerun correctly rejected differing path text; the
  final check displays the identical baseline path before comparing screenshots.
- Runtime packaging tests: 5 passed; release asset tests: 4 passed; image workflow
  registration tests: 4 passed. Python compilation and diff whitespace checks pass.
- Windows CI now measures three launches of the complete pre-tag installer and
  requires every sample to be at most two seconds, retaining logs and JSON.
  This describes the added check; it does not claim a remote CI run has passed.

## Reproduction

Run on Windows with Python 3.9+ and Pillow, outside a restricted desktop sandbox:

```powershell
python -X utf8 tests/InstallerStartupSmoke.py --before C:\bench\before.exe --after C:\bench\after.exe --output work/startup-comparison --rounds 3 --before-rounds 1 --max-ready-seconds 2 --capture-carousel
```

For the current package alone, omit `--before` and `--before-rounds`. For an
additional comparison with saved baseline screenshots, use `--reference-images`
pointing to their directory. Do not run an installed app holding the production
installation mutex when measuring a production installer.

Local raw evidence is retained under `work/startup-dev2`: `comparison` contains
the complete original-layout timing and screenshots; `direct-final` and
`acceptance` contain the final timing and image checks. The early red checks,
intermediate results and failed screenshot comparisons are retained as well.

## Installation progress animation follow-up

The active installation arc previously used a static PNG. It now cycles through
12 frames using the existing 100-millisecond carousel timer while the installing
page is active and the wizard is enabled. Pending steps retain their static
empty rings; the spinner stops updating on other pages, and cached frames are
freed after the timer is stopped on exit. Frame zero matches the original active
icon pixel for pixel. The 12 additional PNG files total 6,028 bytes.

The focused native test slows only its owned fixture's progress callbacks, so it
can capture multiple actual installing-page frames even on fast disks. Before
the fix, the test failed because the active ring never changed. After the fix,
the six focused checks passed, including at least three distinct active-ring
images and identical pending-ring images. No completion-readiness checks were
removed from the ordinary suite; the animation fixture bypasses the unrelated
completion-gate injection. CI runs both suites and retains their evidence.

The final ordinary suite passed all 72 checks again. The complete installer with
the spinner opened in 1.3163325, 1.2458941 and 1.2612096 seconds, passing the
two-second target. Its five-role manual selection and complete automatic cycle
also passed. Raw evidence is in `work/startup-dev2/spinner-acceptance-final` and
the installation/animation fixture directories named in the corresponding logs.

Adding the frame assets legitimately changes Inno's required-disk-space number.
The follow-up image comparison excludes only that native label's bounds using
`--allow-disk-space-change`; layout, artwork and other text remain compared.
It retries unsettled layered-button captures and retains those raw retries.
The final settled screenshots match the baseline outside that dynamic value.
