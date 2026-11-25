# Third-Party Notices

Division Engine is licensed under the MIT License (see `LICENSE.txt`). It
includes or depends on the following third-party components, each governed by
its own license. This file lists direct dependencies; transitive dependencies
carry their own licenses as well.

## Code included in this repository

### Phacelle Noise and Terrain Erosion Filter

- **Files:** `DivisionEngine.Core/Rendering/Terrain/TerrainRendering.cs`
  (the Phacelle noise and `ErosionFilter` portions)
- **Author:** Rune Skovbo Johansen
- **Source:** <https://www.shadertoy.com/view/wXcfWn> ("Advanced Terrain Erosion Filter")
- **License:** Mozilla Public License, v. 2.0 — <https://mozilla.org/MPL/2.0/>

These portions remain under MPL-2.0 and are not relicensed by this project. The
remainder of that file, and the rest of Division Engine, is MIT. The original
technique derives from Clay John (<https://www.shadertoy.com/view/MtGcWh>) and
Fewes (<https://www.shadertoy.com/view/7ljcRW>).

## Runtime dependencies

| Component | Version | License |
|---|---|---|
| [Avalonia UI](https://avaloniaui.net/) | 12.1.x | MIT |
| [Silk.NET](https://github.com/dotnet/Silk.NET) | 2.23.0 | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (wraps Skia, BSD-3-Clause) | 4.155.0 | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [ReactiveUI.Avalonia](https://github.com/reactiveui/ReactiveUI) | 12.1.6 | MIT |
| [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons) | 3.0.2 | MIT |
| [Microsoft.CodeAnalysis.CSharp (Roslyn)](https://github.com/dotnet/roslyn) | 5.9.0 | MIT |
| [NVorbis](https://github.com/ioctlLR/NVorbis) | 0.10.5 | MIT |
| [System.Management](https://github.com/dotnet/runtime) | 11.0.0 | MIT |
| [Avalonia.Fonts.Inter](https://github.com/rsms/inter) (Inter typeface) | 12.1.6 | SIL Open Font License 1.1 |
| .NET runtime and SDK | 10.0 | MIT |

## First-party packages

`NorthRoc.DivisionCompute` and `NorthRoc.DivisionMath` are published by
NorthRoc Studios from separate repositories and are licensed there.
