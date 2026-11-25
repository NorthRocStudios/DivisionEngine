# Contributing to Division Engine

Thanks for looking. Division Engine is an alpha maintained by one person, and it is
deliberately opinionated: signed distance fields instead of meshes, shaders written in
C#, compute-driven rendering. Contributions that fit that direction are very welcome.
Contributions that fight it will probably be declined, and it's not personal.

## Before you start

- **Windows 10/11.** The renderer is Direct3D 11 only right now.
- **A D3D11-capable GPU.** Software rendering is far too slow for the raymarcher.
- **.NET 10 SDK.**
- **`fxc.exe`** from the Windows SDK. Shaders are compiled at runtime; the first build
  of the raymarch shader takes minutes, and the output is cached on disk.

## Build and run

```powershell
dotnet build DivisionEngine.sln
dotnet run --project DivisionEngine\DivisionEngine.Editor.csproj
```

The first launch can look like a hang while the shader cache warms up. It isn't.

## The test scenes are the test suite

There are a handful of unit tests in `DivisionEngine.Core/Tests` covering data and systems,
but the rendering story is the CLI. It renders fixed scenes and writes reference images plus
timing logs into `artifacts/`. Those scenes are the project's real regression tests:

```powershell
DivisionEngine.Editor.exe --cli help          # list every command
DivisionEngine.Editor.exe --cli selftest      # the broad one
DivisionEngine.Editor.exe --cli ctsstest      # cone-traced supersampling
DivisionEngine.Editor.exe --cli denoisetest   # reflection/refraction denoiser
DivisionEngine.Editor.exe --cli arealighttest # area light soft shadows
DivisionEngine.Editor.exe --cli terraintest   # terrain bake + streaming
DivisionEngine.Editor.exe --cli voxeltest     # voxel SDF clipmap
DivisionEngine.Editor.exe --cli shadowtest
DivisionEngine.Editor.exe --cli nodetest      # node graph evaluation
DivisionEngine.Editor.exe --cli bench         # timings
DivisionEngine.Editor.exe --cli compare       # diff against a reference
```

If you change anything in `DivisionEngine.Core/Rendering`, run the matching scene before
and after and say so in the PR. A change that looks correct and shifts every pixel is not
correct.

## Code style

- License headers are enforced by `DivisionEngine.sln.licenseheader`. New files carry it;
  don't strip it from existing ones.
- Everything is **MIT**, except `DivisionEngine.Core/Rendering/Terrain/TerrainRendering.cs`,
  which contains MPL-2.0 code derived from Rune Skovbo Johansen's erosion filter. If you
  touch that file, leave its notice intact. See `THIRD_PARTY_NOTICES.md`.
- Nullable reference types are enabled. Warnings are tolerated but new ones are not.
- Match the surrounding code. There is no formatter config to argue with.

## AI-assisted contributions

Allowed, no disclosure required - the maintainer uses an assistant too. The bar is the
same for everyone: the change must build, must be reasoned about by a human, and must come
with whatever verification the area needs (a test scene result at minimum).

What gets rejected is not "AI wrote this." It's "nobody can explain why this code is here."

## Pull requests

- **Keep them small and single-purpose.** One feature or one fix.
- **Don't reformat unrelated files.** A 200-file whitespace diff will be closed unread.
- **Say what you ran.** "Built on Windows 11, RTX 3060, `--cli ctsstest` before/after" is the
  ideal sentence.
- If it's a big change, open an issue or a discussion first so we don't both waste an evening.

## Where to ask

Open a [discussion](https://github.com/NorthRocStudios/DivisionEngine/discussions) for
questions and ideas, or an issue for something concrete and actionable.
