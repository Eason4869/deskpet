# 0.11.0-dev.2 live language acceptance

Base: live GitHub dev and v0.11.0-dev.1 both 574cad11ef353c2c05e18d07910b549c03c25726, verified 2026-10-26.

## Stage 1 — catalog and persistence foundation

- WPF LanguagePackTests baseline: 320 passed.
- LiveLanguageTests: 15 passed (C# framework and portable).
- UiLanguageChangeTests: 6 passed (C# framework and portable).
- Portable/Desktop builds: existing CS0649/CA1416/nullable/obsolete warnings recorded, no new API suppression. Network vulnerability-check NU1900 is a local network limitation.
- The legacy language entry point still saves pending preference in this stage. UI hot switching and restart-copy removal follow in stage 2.
- New focused Windows/macOS CI is registered separately so core failures do not wait for the full UI/installer suite.
- No macOS real-device acceptance, merge, tag or release claimed.

## Stage 2 — existing controls and guide

- WPF: live UI across 13 catalogs 28 assertions; main app shell 86; guide bookends 49; language pack embedding/startup 320.
- Avalonia: same windows/services and dirty text/selection plus navigation in 13 locales 67; guide preparation 34; existing Japanese language UI 23.
- Data-management Windows CI exposed a busy-gate race at the fixture's immediate deferred-save retry. The fixture now verifies busy rejection and drains its own queued relationship/keepsake work before idle retry; 72 assertions pass locally. Production busy rejection was retained.
- Four original locale packs retain CRLF per .gitattributes; the other nine use canonical LF so source and embedded SHA256 are identical on CI.
- Restart controls/notices are removed from production language switching. Three unused historical keys remain explicitly retired to preserve immutable translation evidence; relationship recovery restart remains available.
- This stage does not yet establish request language snapshot propagation or real macOS device/input-method acceptance.
