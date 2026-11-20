# Archive reader dependencies

The dependencies are pinned to the NuGet packages below. The Framework build uses the existing `vendor/markdown` copies of System.Memory, System.Buffers, System.Numerics.Vectors and System.Runtime.CompilerServices.Unsafe. The .NET 10 build uses the package's dependency-free `net10.0` asset.

| NuGet package | Package SHA-256 |
| --- | --- |
| [SharpCompress 0.50.10](https://www.nuget.org/packages/SharpCompress/0.50.10) | `7bd1a0fb3f88580a4e93377d7fe2f85da75f9f35e056e48fd893632e484cd00c` |
| [Microsoft.Bcl.AsyncInterfaces 8.0.0](https://www.nuget.org/packages/Microsoft.Bcl.AsyncInterfaces/8.0.0) | `f5a5a68b03092ab2abf68843d4a4aea25dfbcbe8dd0f13c625cb779b6fc1927c` |
| [System.Text.Encoding.CodePages 8.0.0](https://www.nuget.org/packages/System.Text.Encoding.CodePages/8.0.0) | `7e308b41cd4f456d08c7921995d8345cabfe2750ea3d27eef698ccc8d069edd1` |
| [System.Threading.Tasks.Extensions 4.5.4](https://www.nuget.org/packages/System.Threading.Tasks.Extensions/4.5.4) | `a304a963cc0796c5179f9c6b7d8022bbce3b2fa7c029eb6196f631f7b462d678` |

| File | NuGet asset path | SHA-256 |
| --- | --- | --- |
| `net48/SharpCompress.dll` | `lib/net48/SharpCompress.dll` | `4a5e04b2e1eeae833373bbf698b450127408ba2367e595c0bec3e7b8a30a8720` |
| `net10.0/SharpCompress.dll` | `lib/net10.0/SharpCompress.dll` | `4651ec4c6aa97222e09ea20422a095adea484497566991827c1d005ce48b1531` |
| `net48/Microsoft.Bcl.AsyncInterfaces.dll` | `lib/net462/Microsoft.Bcl.AsyncInterfaces.dll` | `80678203bd0203a6594f4e330b22543c0de5059382bb1c9334b7868b8f31b1bc` |
| `net48/System.Text.Encoding.CodePages.dll` | `lib/net462/System.Text.Encoding.CodePages.dll` | `b77ee852824a6e8098fbade000beb42ce9df32fa4189a00d5e7f472f6c6a5030` |
| `net48/System.Threading.Tasks.Extensions.dll` | `lib/net461/System.Threading.Tasks.Extensions.dll` | `4f81ffd0dc7204db75afc35ea4291769b07c440592f28894260eea76626a23c6` |

The SharpCompress MIT text comes from the NuGet package's [source revision](https://github.com/adamhathcock/sharpcompress/blob/760eef37ab0b31162065ac16e8923ea6f09d896c/LICENSE.txt). The Microsoft license and notice files come from their respective NuGet packages.
