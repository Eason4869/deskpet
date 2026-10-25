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
