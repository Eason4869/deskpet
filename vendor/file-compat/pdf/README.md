# PDF managed dependencies

PdfPig 0.1.16 comes from the [official NuGet package](https://www.nuget.org/packages/PdfPig/0.1.16), built from [commit a7bb356](https://github.com/UglyToad/PdfPig/tree/a7bb35662bbbf405efddad50aedc9bcdcf515afc) and licensed under Apache-2.0. The package SHA-256 is `d67171846ea8c28f50359137065fec4514266d7a32b23eae6c5f2ebed8ffcfc4`. The upstream license and third-party notices accompany these assemblies.

The Framework reader uses `lib/net471`, plus `lib/net462/Microsoft.Bcl.HashCode.dll` from the [official Microsoft.Bcl.HashCode 6.0.0 package](https://www.nuget.org/packages/Microsoft.Bcl.HashCode/6.0.0) under the MIT license. The Microsoft package SHA-256 is `f3b9b2bab0bf8cc717d5fdf6d7aee3ec54e36d9e85bd41347acae161319cbd6b`. The Framework reader reuses the System.Memory and System.Buffers assemblies already distributed by DeskPet for Markdown. The portable reader uses `lib/net9.0`, which has no additional package dependencies. Neither reader references the empty NuGet packaging marker `UglyToad.PdfPig.Package.dll`.

| Assembly | Framework SHA-256 | Portable SHA-256 |
| --- | --- | --- |
| Microsoft.Bcl.HashCode.dll | `3a4e851ee5fc0f6182aa5a3d65dc56fcd6979b65334b5c3b92fbdc791457c0ab` | |
| UglyToad.PdfPig.Core.dll | `894bf5e8daac5e4f6fbd7e2eb26c6b2f39e42b3122e35fa69c6fa30469a43bb0` | `cbdd1809ee0f4ba617900845d182e5e67183377fe5848a8d9d1222b3c395116b` |
| UglyToad.PdfPig.dll | `cd712f405cbd4400903d18f2855e0b2458acb76d75019765f9faaa2f3ba0717e` | `c47abebdffebb6e48a1d40f4c22ce5f673c57558ecdb3187841b728112dea279` |
| UglyToad.PdfPig.DocumentLayoutAnalysis.dll | `aa79f1774b74e5bd6939e089bb7770fd62b8e8c22d444f5f736a7171c243e16c` | `c0d1e237eabcb2c741fc7ab7b26c255af6f5751d1cd7e86d7ae613611f79bc88` |
| UglyToad.PdfPig.Fonts.dll | `b066e7440e7d76d2b8229e9274e300dcfe7dcec65dd578106e8a1bf2473bb911` | `30521d2aa02252c0097cd3a9b12832368d5c925e1ccb4bc3a5735671bd3d2e1e` |
| UglyToad.PdfPig.Tokenization.dll | `84315ce24887373ed9019442edfcb1b7777e7782ed0c4bf69be63e84941b43e0` | `539d0ca2c7981b9eaf8541e17434911042f7cc73d3abc24962994bb99472ed3c` |
| UglyToad.PdfPig.Tokens.dll | `d91a3f93ca27728709875ef71425d4c8e7165d5d3a7b13094ec976cfc22d305c` | `d6fbd73a520bb961ffcc98030ab686f43c88b03f5b059ba11e3781e0f091433d` |
