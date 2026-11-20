# Word binary container dependency

The Word 97–2003 reader uses OpenMcdf 3.4.0 to open Compound File Binary streams read-only. The portable build uses the dependency-free `lib/net10.0` assembly. The .NET Framework build uses the `lib/netstandard2.0` assembly, the .NET Framework netstandard façade, and the existing Microsoft.Bcl.HashCode and System.Memory dependencies from the PDF and Markdown readers. The public package binaries remain unchanged.

| Artifact | SHA-256 |
| --- | --- |
| Official NuGet package | `9ce74b7014f0354ca24bd9e95d4106c8fced6beeb6d1e7160c2312a6b029ce8f` |
| `net10.0/OpenMcdf.dll` | `0480207b9d6312262cd807a8bd66265f467d2e033c8aa37c55b3b337524ea97e` |
| `netstandard2.0/OpenMcdf.dll` | `ec390a356653b54c75bfcf6375ee918a9d4bf4ab9094a6932ae3f548fbdfd016` |

OpenMcdf is distributed under the Mozilla Public License 2.0. The full corresponding source is available at the pinned upstream revision. [Official NuGet package](https://www.nuget.org/packages/OpenMcdf/3.4.0) · [Source revision](https://github.com/openmcdf/openmcdf/tree/687fae7286167035a2a1f2c7ab39720679a187a5) · [MPL 2.0](https://www.mozilla.org/MPL/2.0/)
