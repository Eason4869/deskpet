# Windows installer presentation

`DeskPet.iss` owns installation, identity/version protection, migration and
uninstall. `InstallerUi.iss` lays out native Inno pages and owns the carousel
timer. `NativeStyle.iss` places deterministic artwork beneath real native
buttons at nonzero alpha (1/255), retaining mouse hit testing, keyboard focus,
accessible captions and the original event handlers. A visible focus outline
follows the native focused control. The progress fill is a rounded native HWND
whose width comes only from `CurInstallProgressChanged`.

`FinishOptions.iss` applies interactive desktop/startup choices only on Finish.
The startup command exactly matches `src/StartupRegistration.cs`; settings JSON
is never edited. Foreign or modified links and Run entries remain untouched.
Silent installs retain `/TASKS=desktopicon`, never launch, and bypass this UI.
New desktop task entries defer uninstall removal to the ownership check.
For old uninstall logs that recorded unconditional deletion, a narrowly scoped
same-directory backup preserves a modified desktop link byte for byte. Backup
failure aborts uninstall; restore never overwrites a newer destination, and a
failed restore retains the backup and reports its location.

Artwork provenance: `assets/{e-bao,dafeiyu,marfu,xiaobei,gugugaga}.png` are the
official package portraits normalized during companion packaging. The app logo
is referenced directly from `../assets/deskpet-logo.png`. Decorations and the
blue success badge reproduce the approved installer preview. Regenerate the
committed panels and small PNG decorations with:

```powershell
python tools/generate_installer_artwork.py
```

Pillow is needed only for regeneration. Production compilation and installation
use committed PNGs and native Inno Setup 6.7+; no browser/runtime download is
required. `Build.ps1` retains its UTF-8 BOM.

Run `python -X utf8 tests/InstallerUiSmoke.py --compiler <ISCC.exe>` to compile
the actual production script with a unique fixture AppId, mutex, Run key and
shortcut locations. It uses explicit `CreateProcessW` desktop binding (Python's
`subprocess.STARTUPINFO` does not marshal an assigned `lpDesktop` attribute),
captures DPI-aware native pages, installs to owned Chinese/space paths and
checks real completion/silent/ownership/uninstall behavior. The fixture uses
the normal production loader; it cannot enable fixture identities in a release
build. Default and reset paths are rewritten into that root, ordinary launches
require exactly one bounded `/DIR`, and a fixture-only `PrepareToInstall`
invariant refuses targets outside it before directory creation or file
retirement. The deliberately named no-`/DIR` reuse mode first validates the
exact fixture registration; its default is a different owned directory, so
the reuse assertion cannot pass merely because a command-line flag set it.
Evidence lives in the owned `work/installer-ui-*` directory. Native mouse
messages and focused Space-key messages drive controls; screenshots do not
claim physical pointer/keyboard input.
