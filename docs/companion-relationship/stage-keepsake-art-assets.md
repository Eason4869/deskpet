# Stage keepsake assets

## Local title fonts (F4a)

Only title fonts are registered here so far. Scene selection/integration is a separate F4b/H1 gate. These modified, static, regular title-only subsets are renamed `DeskPet Keepsake <region>`; no system fonts are redistributed or installed. Names, body copy and dates retain their existing UI fonts.

Official upstream pins: `notofonts/noto-cjk@f8d157532fbfaeda587e826d4cd5b21a49186f7c` and `google/fonts@e7db6ca603fd4862010383ff17ffd6cc4ffac1c0`. The source URLs below use those immutable commits, not branch heads. Source files stay in the asset preparation cache and are not bundled.

|Face / internal family|Official source / version|Full source bytes / SHA256|Final file / bytes / SHA256|
|---|---|---|---|
|SC / `DeskPet Keepsake SC`|[source](https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/SimplifiedChinese/NotoSerifCJKsc-Regular.otf) / Version 2.003;hotconv 1.1.1;makeotfexe 2.6.0|24543080 / `2a2eae2628df83556c54018c41e20fa532c1b862c5256ae8b3f23feb918d12ca`|`assets/fonts/relationships/deskpet-keepsake-sc.otf` / 21476 / `db62a03ccb65b1a4169462c9f3ee626850a3cb0868fa7b067cb1339745ab9fbd`|
|TC / `DeskPet Keepsake TC`|[source](https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/TraditionalChinese/NotoSerifCJKtc-Regular.otf) / Version 2.003;hotconv 1.1.1;makeotfexe 2.6.0|24541904 / `234301038e76e7c35c43113785024700c4e4fe7bdce1d1fbbc42fca7e6683798`|`assets/fonts/relationships/deskpet-keepsake-tc.otf` / 28632 / `35afdbc493656ad73fd27c8902c4bd5e48eb696c43ed1c94c5b5c579ac71d9f7`|
|JP / `DeskPet Keepsake JP`|[source](https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/Japanese/NotoSerifCJKjp-Regular.otf) / Version 2.003;hotconv 1.1.1;makeotfexe 2.6.0|24573864 / `d9854c7a8ef170b5a7932558856fd64eb8de0b007cd823fed6f9f514ad2803d3`|`assets/fonts/relationships/deskpet-keepsake-jp.otf` / 30084 / `c29ef223d1a0cefffb49d528b3b6604b232dcff2a8fc56a4ac2750323ca668df`|
|KR / `DeskPet Keepsake KR`|[source](https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/Korean/NotoSerifCJKkr-Regular.otf) / Version 2.003;hotconv 1.1.1;makeotfexe 2.6.0|24539640 / `77b4b741f864d27f15e90f275b17106dde90b2ad28f82bab72dc95805db5fb42`|`assets/fonts/relationships/deskpet-keepsake-kr.otf` / 26452 / `27dc382acabc9a109eebfca81c8533f3e537a14e65e7fb1c1999ecbc30b35893`|
|Latin / `DeskPet Keepsake Latin`|[source](https://raw.githubusercontent.com/google/fonts/e7db6ca603fd4862010383ff17ffd6cc4ffac1c0/ofl/notoserif/NotoSerif%5Bwdth%2Cwght%5D.ttf) / Version 2.015|1887192 / `4d8e6761424656867019081a1a01336f3cb086982682698714054fc33f782713`|`assets/fonts/relationships/deskpet-keepsake-latin.ttf` / 16136 / `15f41d10b386e7a23edb029a53afacb82cd9406b70ae116e48ecaa7eabd190e3`|
|Devanagari / `DeskPet Keepsake Devanagari`|[source](https://raw.githubusercontent.com/google/fonts/e7db6ca603fd4862010383ff17ffd6cc4ffac1c0/ofl/notoserifdevanagari/NotoSerifDevanagari%5Bwdth%2Cwght%5D.ttf) / Version 2.006|757692 / `1191e07bfeb062d80e252eb85b0eafdfbda1e350707a2a60628668e8f677dbbb`|`assets/fonts/relationships/deskpet-keepsake-devanagari.ttf` / 23944 / `3a307f72ffd1bb4d71b1bbe291727fcb73631e881e17911f5dbf4e3704150ef1`|

Total full sources: **100843372 bytes**. Final six fonts: **146724 bytes** (about 143.3 KiB). The application embeds the subsets and three OFL files, not the full sources, asset manifest, or corpus. Runtime resources add a small resource container overhead; actual EXE/ZIP/package deltas belong to M6.

### Licenses and modification

All six sources carry SIL Open Font License 1.1. Original copyright/name records and embedded OFL references remain in the fonts; modified family, typographic family, full, unique and PostScript names are replaced, and unused variable-font PostScript prefixes are removed. No claim of legal advice or additional rights beyond the recorded licenses is made.

CJK copyright retained in name ID 0: © 2017–2024 Adobe (http://www.adobe.com/). Latin and Devanagari retain Copyright 2022 The Noto Project Authors and their respective upstream project URLs. The separate upstream license files are retained verbatim:

|Local notice|Pinned source|SHA256|
|---|---|---|
|`assets/fonts/relationships/OFL-CJK.txt`|[official notice](https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/LICENSE)|`6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2`|
|`assets/fonts/relationships/OFL-Latin.txt`|[official notice](https://raw.githubusercontent.com/google/fonts/e7db6ca603fd4862010383ff17ffd6cc4ffac1c0/ofl/notoserif/OFL.txt)|`cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a`|
|`assets/fonts/relationships/OFL-Devanagari.txt`|[official notice](https://raw.githubusercontent.com/google/fonts/e7db6ca603fd4862010383ff17ffd6cc4ffac1c0/ofl/notoserifdevanagari/OFL.txt)|`a216f6f8d85c7228093e0ee5e258d9d377e6671f68acb4db1930b29583d0f331`|

See also `docs/third-party-notices.md`. WPF embeds the notices as Resource streams alongside the fonts; Avalonia embeds the original files as unconditional AvaloniaResource entries. Both were read from built production resource configurations.

### Reproducible preparation and coverage

Asset preparation uses Python 3.12, FontTools **4.62.1**, uharfbuzz **0.53.0** only in an owned workspace target. Neither Python nor these tools become application runtime dependencies.

```powershell
python -m pip install --target work/f4a/python -r tools/fonts/requirements.txt
$env:PYTHONPATH=Join-Path $PWD 'work/f4a/python'
$env:PYTHONIOENCODING='utf-8'
python tools/fonts/relationship_title_fonts.py --build
python tools/fonts/relationship_title_fonts.py --check
python tools/fonts/test_relationship_title_fonts.py
```

`--build` fetches only the pinned raw font/license URLs when absent from `work/f4a/upstream`, rejects changed source SHA256, instantiates variable Latin/Devanagari axes at `wght=400, wdth=100`, and uses FontTools Subsetter with `layout_features=["*"]`, all original name/license records, `.notdef` outlines, recommended glyphs, Unicode cmap coverage and complete layout closure. CJK sources are static regional Regular OTFs. The internal name replacement is part of this same script. Exact options and immutable source hashes are checked in with the script; `manifest.json` records source and final hashes, versions, bytes, codepoints and shaping-support scalars. Re-running the pinned build produced identical font/license/corpus/manifest/generated-C# bytes.

`corpus.json` contains the actual eight printed heading/title values (six stage keys, generic `relationship.card.title`, fixed future `relationship.card.halloweenTitle`) in all 13 shipped locale files. A locale maps to SC/TC/JP/KR/Devanagari or Latin; Latin includes all actual punctuation and accent codepoints occurring in these localized titles. The validator compares fresh source translations against the recorded corpus before checking each cmap, exact static family, final font/license hashes, original copyright, generated C# coverage and Hindi layout tables. A regression test supplies a future Japanese stage translation to the actual validator and verifies a clear regeneration failure. The validator is offline; comparison with full-source Hindi shaping is an extra check when the pinned source is already cached, and reports a skip when the cache is absent.

**WPF Indic shaping requirement:** a literal title-only subset initially passed HarfBuzz but WPF rendered direct cmap glyphs. The same native fixture shaped the full static upstream font correctly. Preserving only RA U+0930 and VIRAMA U+094D as explicit script-support scalars plus their GSUB/GPOS closure repairs WPF native shaping. These two supporting scalars are recorded under `shaping_support`; they are not extra printed copy. Both native TextBlocks now produce a nontrivial shaped glyph sequence, and the cached full source and subset have matching HarfBuzz positions/counts. Do not remove these scalars based solely on visible title characters.

Existing non-Chinese stage titles are still English in several shipped locale files. Coverage is for their actual current strings; this does not establish completed stage translation or human native-language review. The new Halloween values are explicitly fixed product copy (Chinese/Traditional Chinese/Japanese/Korean/Hindi/Portuguese localized names; common Halloween name elsewhere), with no human review claim. Translation changes require regeneration and later layout review.

### Runtime seam and native proof

Shared `RelationshipTitleFonts.ForLocale(locale)` returns an immutable region/file/family descriptor and `Covers(title)`; `RelationshipTitleFonts.Version` is `relationship-title-serif-1`. Both host-local `RelationshipTitleFont.FontForLocale(locale,title)`, `GetTypeface(locale,title)` and `TryGetBundledTypeface(locale,title,out typeface)` are available for F4b. A whole unsupported title uses the existing normal UI family instead of silently substituting another font as a successful bundled face. Unicode scalar iteration handles surrogate pairs and rejects malformed/unsupported scalars; no arbitrary user name is added to these subsets.

WPF `tools/fonts/Build-WpfTitleResources.ps1` uses a bounded six-file allowlist and ResourceWriter stream entries. `Build.ps1` embeds `<actual output basename>.g.resources`, including the Unicode production App name and test executable names. Resource keys are lowercase `assets/fonts/relationships/<file>`. The local resolver uses `pack://application:,,,/<own assembly>;component/` plus `./assets/fonts/relationships/#<family>`, then validates the resolved GlyphTypeface family and each actual glyph. This follows the [WPF Resource-font requirements](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/packaging-fonts-with-applications); ordinary font EmbeddedResource entries are not used.

Avalonia unconditionally includes `assets/fonts/relationships/*.otf`, `*.ttf`, `OFL-*.txt`, linked under `Assets/fonts/relationships/`. Family URI: `avares://DeskPet/Assets/fonts/relationships#DeskPet Keepsake <region>`; actual resource path: `avares://DeskPet/Assets/fonts/relationships/<file>`. The resolver verifies FontManager's real GlyphTypeface family and cmap. No global FontManager replacement or registration is used. See [official custom font documentation](https://docs.avaloniaui.net/docs/styling/custom-fonts).

Focused proof: all 13 current locale corpora and both themes rendered to 26 title sheets per host (52 PNGs), with actual family/cmap checks, missing-resource/invalid-family/unsupported-scalar fallback checks, native Hindi shaping checks, and font/license byte checks from built resources. WPF also loaded all six fonts from the actual production App assembly without starting its entry point. Sources remain in `work/f4a/title-proofs/{wpf,avalonia}/`; these are title-only evidence, not complete card/layout/contrast acceptance. F4b must apply the helper only to card title elements, set appropriate language metadata, include the font/style version in capture invalidation, and verify complete scene/ornament layouts. Native macOS hardware and final EXE/ZIP installation remain unverified.
