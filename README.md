<div align="left">

<img width="128" height="128" alt="DivisionEngineLogoR" src="https://github.com/user-attachments/assets/4495b5e3-012d-42e4-8e04-1eecbf22ee1d" />

# Division Engine

[![Version](https://img.shields.io/github/v/release/DivisionEngine/DivisionEngine?label=Version&include_prereleases)](https://github.com/DivisionEngine/DivisionEngine/releases)
[![License](https://img.shields.io/github/license/DivisionEngine/DivisionEngine)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![AvaloniaUI](https://img.shields.io/badge/Avalonia-UI-blue)](https://avaloniaui.net/)

</div>

Division Engine is an SDF-based game engine written entirely in C#. Utilizing Avalonia UI for the interface and Silk.NET for native rendering, Division Engine features a comprehensive shader pipeline that dynamically builds HLSL shader code from .NET code, thanks to [DivisionCompute](https://github.com/NorthRocStudios/DivisionCompute).

*Note: This engine is still in preview and has known issues; it is specifically built for experimentation and education only.*

Rendering is compute-driven: shaders are written as ordinary C# structs, translated to HLSL on demand by DivisionCompute, and dispatched through its Direct3D 11 backend, while Silk.NET's OpenGL context presents the finished frame. The vector and matrix types shared by CPU code and shaders come from [DivisionMath](https://github.com/NorthRocStudios/DivisionMath).

Picture this:
- SDF-based rendering
- GPU compute acceleration in C#
- Open source
- ECS data-oriented setup
- Convenient editor tooling

## What Are SDFs?

*Signed Distance Fields* are spatial fields that store information represented as a grid sampling of the closest distance to the surface of an object defined as a polygonal model. Usually, the convention of using negative values inside the object and positive values outside the object is applied. Signed distance fields are important in computer graphics and related fields. Often, they are used for collision detection in cloth animation, soft-body physics effects, malleable geometry, volumetric effects, and fluid simulation.
(https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-34-signed-distance-fields-using-single-pass-gpu)

## How to Work with ECS

*ECS* or an entity-component-system framework is a way of organizing game data such that it is memory efficient and hyper-performant. Entities are simply IDs with components stored as a dictionary in an "ECS World" object. Systems are code files written that operate on an awake --> update --> fixed update --> render schedule, allowing components to be manipulated during different engine loops/stages. For more information on ECS, check out how the Unity game engine implemented its ECS framework here: https://unity.com/ecs

## Framework

Division Engine is built on four core packages: Silk.NET, DivisionCompute, DivisionMath, and AvaloniaUI.
Check them out here:
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [DivisionCompute](https://github.com/NorthRocStudios/DivisionCompute) - C# to HLSL source-to-source compiler and Direct3D 11 compute runtime
- [DivisionMath](https://github.com/NorthRocStudios/DivisionMath) - vectorized math library built for near 1:1 parity with HLSL
- [AvaloniaUI](https://github.com/AvaloniaUI/Avalonia)

> **A note on ComputeSharp:** earlier previews of Division Engine built shaders with [ComputeSharp](https://github.com/Sergio0694/ComputeSharp). The engine has since migrated to DivisionCompute and DivisionMath: shader translation, bytecode compilation, resource binding, and dispatch all live in DivisionCompute, and no engine project references ComputeSharp anymore. The new stack targets Direct3D 11 (HLSL compiled to DXBC at runtime, cached on disk between runs) and shares one math API between CPU and GPU code.

## Resources:
Follow the development: https://trello.com/b/mWtyHBMf/division-engine

Tutorials by Inigo Quilez (Not sponsored, just useful for learning constructive geometry):
- Build mathematical worlds: https://youtu.be/0ifChJ0nJfM?si=ypKU1rz-8JloPlj2
- Build a 3D landscape: https://youtu.be/BFld4EBO2RE?si=EASXvq-ez2qBOIHN
- Paint a 3D character with math: https://youtu.be/8--5LwHRhjk?si=fH9QwvCz6dLptHE1

## Editor Preview Screenshots:

Reflective Menger Sponge:
<img width="1919" height="1031" alt="Screenshot 2026-11-09 021502" src="https://github.com/user-attachments/assets/6044d156-ba3c-4a32-a943-e6a2c83050a9" />

Reflective and Refractive Glass Objects:
<img width="1919" height="1029" alt="Screenshot 2026-02-12 015857" src="https://github.com/user-attachments/assets/7fe0f9b6-5a65-4160-ad74-874034a14ee6" />

Texturing System:
<img width="1919" height="1029" alt="Screenshot 2026-07-18 013607" src="https://github.com/user-attachments/assets/86c5ae53-8e12-4164-98dd-418f4ff5bae1" />

Multiple Workflows:
<img width="1919" height="1028" alt="Screenshot 2026-09-02 020709" src="https://github.com/user-attachments/assets/353f6bb6-527a-4336-99b2-01b55e936383" />

## License:
Division Engine is free and open-source software licensed under **GNU General Public License v3.0 (GPL 3.0)**

#### You may:
- Use it for any purpose (commercial or non-commercial)
- Modify it to fit your needs
- Distribute copies to others
- Sell it (but see requirements below)

#### You must:
- Include the original copyright notice
- State significant changes you make
- Disclose source when you distribute
- License derivatives under GPL 3.0

### Commercial Use

Yes, you can use Division Engine commercially! The engine is free for any use.

*The only requirement*: If you distribute a modified version of the engine itself (not your game assets or code), those modifications must be shared under GPL 3.0.

## Node Editor

The editor ships with a reusable node graph window (`NodeEditorWindow` plus `NodeEditorWindowViewModel`) for building graph-based tools. It supports typed input/output ports, live node previews, inline value editing, drag-to-connect wiring, pan/zoom, undo/redo, search and context-menu node creation, and JSON graph serialization.

Open **View → Examples → SDF Playground (Example)** to try the example graph. The View menu lists every editor window; the **+** button on each panel lists only the windows relevant to that panel. To build a new editor (geometry, materials, procedural audio, and so on), derive a view model from `NodeEditorWindowViewModel`, seed a `NodeGraph`, and optionally override `CreateNodeViewModel` for custom node visuals. New node types are registered with `[NodeMenuItem(...)]` and can be validated headlessly with:

```bash
DivisionEngine.Editor.exe --cli nodetest
```

## Voxelized SDF Rendering

Division Engine can render the same scene through either the exact SDF or a
world-stable voxel clipmap. Flip the **Voxel SDF** toggle in the main toolbar (or
enable **Voxel SDF Rendering** in the Settings window) to switch live; the next
frame uses the other path. The regular ray-marched path is unchanged and remains
the default.

The voxel path combines the de-voxelization approach from [Ray Tracing of Signed
Distance Function Grids](https://jcgt.org/published/0011/03/06/paper-lowres.pdf)
with the nested-grid LOD idea from Hoppe's [Geometry
Clipmaps](https://hhoppe.com/geomclipmap.pdf), the same structure Mike Turitzin
describes in his engine video:

- Four nested levels use power-of-two cell sizes. Each level is stored in a
  3D atlas and addressed toroidally by **absolute world cell index**, so
  moving the camera changes only which window is active, not where the voxels
  are. A sub-cell camera move does not shift or rebake anything.
- Rendering trilinearly reconstructs the de-voxelized surface and blends adjacent
  levels across a transition band. The nearest level is chosen from camera
  distance; normals are computed analytically from the trilinear patch.
- Updates are incremental and asynchronous. A moving camera only queues the
  newly exposed L-shaped shell; a moved SDF object only queues the levels/cells
  its old and new bounds touch. The queue is drained under a per-frame cell
  budget (2,000,000 cells by default) so streaming does not turn into a full
  rebake on every frame.
- A static scene queues no work at all. The first voxel frame primes all four
  levels once; after that, a fast camera can outrun the finer levels, which crop
  to their currently-ready region while the renderer falls back to a coarser
  level or the exact SDF until the shell arrives.
- Rays that leave all ready levels fall back to the exact scene SDF, which keeps
  infinite planes and distant terrain correct.

The clipmap is a pragmatic middle ground between a dense grid and a full sparse
voxel octree. An SVO would remove more empty storage, but dynamic node updates,
neighbour stitching, and GPU pointer traversal are substantially more complex;
the toroidal regular grids already give world stability, incremental updates,
and LOD with a much smaller implementation surface. Sparse brick allocation per
level remains a possible future optimization.

The voxel path currently supports the core material, lighting, shadow,
reflection, and single-object refraction features. Refraction is a single-object
approximation (caustic shadowing is not reproduced), texture normal maps are not
applied, and procedural skies fall back to the gradient sky. Its simplified
lighting model can also be slightly darker than the exact path. The first voxel
frame compiles the large de-voxelization shader with D3DCompiler's O3 pass; this
can take several minutes once, after which it is read from the shader cache.
Increase **Voxel Grid Resolution** for finer cells (at the cost of memory and
initial bake time), and tune **Voxel Update Budget** for faster or gentler
streaming.

Command-line tests:

```bash
# Static stability, sub-cell stability, cell-boundary streaming, object edits,
# teleport recovery, and exact/voxel image validity
DivisionEngine.Editor.exe --cli voxeltest

# One deterministic voxel screenshot
DivisionEngine.Editor.exe --cli shot --voxel --fixed-frame 2 --out voxel.png

# Colour each pixel by the clipmap level that supplied its hit surface
DivisionEngine.Editor.exe --cli shot --voxel --debug-mode 10 --fixed-frame 2 --out levels.png

# Compare static or moving-camera performance against the exact path
DivisionEngine.Editor.exe --cli bench --voxel --move 0.4 --frames 60
DivisionEngine.Editor.exe --cli bench --no-voxel --move 0.4 --frames 60
```

## Cone-Traced Supersampling (CTSS)

The exact SDF path supports **cone-traced supersampling**, an SDF-specific
anti-aliasing technique from [Cone-Traced Supersampling for Signed Distance Field
Rendering](https://cim.mcgill.ca/~derek/files/publication-files/chubarau-zhao-rao-nowrouzezahrai-kry-cone-traced-supersampling-with-subpixel-edge-reconstruction-ieee-trans-vis-comput-graph-2024.pdf).
Instead of casting a grid of extra rays for every pixel, CTSS traces the primary
ray as a cone whose base covers the pixel. Pixels whose cone is only partially
covered by geometry are resolved with a 2x2 subpixel grid (the paper's CTSS +
supersampling combination, applied adaptively); fully occluded interior pixels
keep the ordinary single-sample trace. The extra shading is therefore
concentrated on geometric edges.

The pure paper formulation was evaluated first: its local half-space visibility
mask over-covers large curved silhouettes, because a ray that only clips an edge
can still penetrate deeply into the object. The adaptive subpixel grid replaces
that approximation and is what this engine ships.

Anti-aliasing is a per-camera choice: select the camera and pick **Anti-Aliasing ->
Anti-Aliasing Method -> CTSS** (or **FXAA** / **None**) in the inspector.
Per-camera CTSS knobs appear below it:

- **Cone Coverage** - cone radius in pixels; 1 traces through the pixel corners.
- **Debug CTSS** - one dropdown for both methods: FXAA threshold/direction and
  CTSS sample-count/coverage visualisations. Options that do not apply to the
  selected method render normally.

Command line:

```bash
# Deterministic CTSS capture (post-processing off to show the raw SDF pass)
DivisionEngine.Editor.exe --cli shot --ctss --no-post --fixed-frame 8 \
    --width 1280 --height 720 --out ctss.png

# Compare coverage / isolate the edge detector
DivisionEngine.Editor.exe --cli shot --ctss --ctss-coverage 0.5 --no-post --out ctss-half.png
DivisionEngine.Editor.exe --cli shot --ctss --ctss-debug 1 --no-post --out edges.png

# Headless quality gate: no-AA vs CTSS against a 4x-SSAA reference
DivisionEngine.Editor.exe --cli ctsstest --width 640 --height 360
```

CTSS is geometric anti-aliasing: it cannot resolve colour discontinuities from
textures, shadows, or specular highlights inside a hit group. The iterated
fractals (Mandelbulb, Menger sponge, Mandelbox) use conservative distance
estimators rather than true Eikonal SDFs. Tracing a cone against them finds false
partial hits, so CTSS detects such objects and falls back to the ordinary
single-sample trace for those pixels: their silhouettes stay aliased, but their
fine detail is preserved. CTSS applies to the exact SDF renderer, not the voxel
clipmap.

### Measured results

Quality gate (`--cli ctsstest`, 640x360, 4x-SSAA reference, reflections and
refractions disabled to isolate geometric edges):

- CTSS changes **0.97% of pixels** (the detected edges).
- On those pixels the mean luminance error drops from **0.0559 to 0.0364**
  (**35% lower**).
- The sphere silhouette improves by **39%**; the full edge mask by 1.1%
  (fractal/texture edges are deliberately left untouched).
- No NaN/Inf pixels.

Performance at 1280x720 with post-processing off (60 measured frames, 10 warmup):

| Renderer | Median (steady state) | FPS |
| --- | ---: | ---: |
| Pre-change renderer (no AA) | 30.3 ms | 32.9 |
| This build, Anti-Aliasing = None | 34.4 ms | 29.1 |
| This build, Anti-Aliasing = CTSS | 28.7 ms | 34.9 |

CTSS is **about 1.2x faster than this build's None path**: interior pixels
take one primary sample instead of the renderer's two, and only detected edges
take four. It runs at parity with (even slightly ahead of) the original renderer,
well inside the paper's 15-30% overhead. Frame times vary by a few milliseconds
with GPU clocks; the table uses the median of repeated 60-frame runs after a
warm-up. The None path still carries a little of the larger shader's register
cost, which is why CTSS is the recommended exact-SDF anti-aliasing choice.

The first optimized shader compile takes about 15 minutes on this machine
(roughly 4 minutes unoptimized); it is cached on disk and later runs start in
under a second.

Cone-edge detection runs in a separate small kernel that writes a mask; the main
SDF kernel reads an SRV copy. That keeps `None`/`FXAA` on their original register
budget. Multi-kernel shaders require DivisionCompute to emit explicit
`register(tN/uN)` slots (the translator change lives in the sibling
`DivisionTranslate/DivisionCompute` checkout this project builds against).

### What about the planned marketplace?

In the future there will be a Division Engine marketplace, completely separate with its own terms. The engine remains free and GPL-licensed. The marketplace will be where creators can buy/sell assets, projects, etc. with revenue supporting ongoing development.

**In short:** You can build anything with Division Engine - free games, commercial games, modded versions - as long as any distributed version (including modified versions) remains under GPL 3.0 with source code available.
*Full license terms are available in [LICENSE.txt](LICENSE.txt)*
