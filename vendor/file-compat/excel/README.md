# Spreadsheet reader dependencies

The Windows build uses the binaries under `net462`. The portable build uses the binaries under `net8.0`. The DLLs are unmodified, managed, and architecture-independent. Both packages use the MIT license.

| NuGet package | Package SHA-256 |
| --- | --- |
| [ExcelDataReader 3.8.0](https://www.nuget.org/packages/ExcelDataReader/3.8.0) | `3f32b9ed2f151cf57bed32da27ae45990724db5b093f017544458f5522277cc1` |
| [ExcelNumberFormat 1.1.0](https://www.nuget.org/packages/ExcelNumberFormat/1.1.0) | `c48af3056098d8f123225b69c16d814f58ff28928e1f6a8fb6fba65fe35366f1` |

| Vendored file | Package entry | SHA-256 |
| --- | --- | --- |
| `net462/ExcelDataReader.dll` | `lib/net462/ExcelDataReader.dll` | `100e86dd342089a57ca810214af5e32f988425d6581c7f87825209c83ade525d` |
| `net8.0/ExcelDataReader.dll` | `lib/net8.0/ExcelDataReader.dll` | `ad3047be1090638e61487f8a8291a1940d77bd99b104084e80d2a2ea4816d86c` |
| `net462/ExcelNumberFormat.dll` | `lib/net20/ExcelNumberFormat.dll` | `03ccc71a42833102ef54ae9ecf39d9d62c0491c17359aa37d1d2ef0ec7397d04` |
| `net8.0/ExcelNumberFormat.dll` | `lib/netstandard2.0/ExcelNumberFormat.dll` | `d7c45b8e4e1027831e2fc8cdf941faf51d28a66feacfcaaa737da73262e7b507` |

The license files come from the upstream release tags. [ExcelDataReader v3.8.0](https://github.com/ExcelDataReader/ExcelDataReader/blob/v3.8.0/LICENSE), [ExcelNumberFormat v1.1.0](https://github.com/andersnm/ExcelNumberFormat/blob/v1.1.0/LICENSE)
