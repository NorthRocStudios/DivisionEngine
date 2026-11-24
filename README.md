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

#### You May:
- Use it for any purpose (commercial or non-commercial)
- Modify it to fit your needs
- Distribute copies to others
- Sell it (but see requirements below)

#### You Must:
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

## Dockable Editor Windows

The editor's four classic panels (World, Environment, Properties/Settings and
Assets/Console) are now a general docking layout. Every panel is a tab group in a tree
of horizontal and vertical splits, so the workspace can be rearranged like Unity or
Visual Studio:

- **Dock into a group** by dragging a tab over the middle of another group. A five-zone
  compass (left, right, top, bottom, centre) shows where the tab will land; dropping on
  an edge creates a new tab group on that side and animates it into place, while the
  centre merges the tab into the existing group. The white insertion line shows the tab
  order for a centre drop, and dragging within one group reorders its tabs.
- **Split a tab into a new group** without dragging with **right-click -> Move to New
  Group -> Left/Right/Above/Below**. The new group gets the dragged tab and inherits the
  source panel's logical kind, so its **+** menu offers the same windows.
- **Float a tab** by dragging it outside every editor window, or with **right-click ->
  Float Tab**. Floating tabs live in normal top-level windows with their own tab strip,
  position and size; they can be moved to another monitor or dragged back onto any dock
  group (edge drops split, centre drops merge). Closing a floating window closes its tabs.
- **Resize splits** with the same splitters as before; any number of nested splits is
  supported. Empty groups show a hint and can be filled from their **+** menu, and groups
  that become empty because their last tab was moved or closed are removed automatically
  (the final group of the main window is kept so the editor always has a place to open
  windows).
- **Tabs** keep the original look and behaviour: drag to reorder, right-click for Close,
  Duplicate Tab, Float/Dock Tab, Move to New Group and Close Tab Group, or middle-click
  to close. The **+** button opens the same window catalog as **View**, filtered to the
  panel kind, and **View** focuses an already-open window instead of duplicating it.
- **Animations** are deliberately short: a new split grows from the drop point and its
  content fades in, the five-zone compass fades, and floating windows fade in and out.
  Nothing changes the existing tab or window design.

Layouts are saved with the project as a dock tree plus the floating windows' bounds.
Projects saved by earlier builds still open with their original four-panel layout: the
old `LeftTabs`/`RightTabs`/`CenterTabs`/`BottomTabs` fields are converted to a dock tree
on load, and the current layout is also written back to those fields for compatibility.

Headless checks for the layout model, drop geometry and serialization:

```bash
DivisionEngine.Editor.exe --cli windowtest
```

The command exercises edge splits, tab moves/reorders, empty-group collapse, floating
groups and docking back, JSON project round-trips, legacy layout conversion, unknown
window types and the drop-zone geometry rules, and exits non-zero on any failure.

## Voxelized SDF Rendering

Division Engine can render the same scene through either the exact SDF or a
world-stable voxel clipmap. Flip the **Voxel SDF** toggle in the main toolbar (or
enable **Voxel SDF Rendering** in the Settings window) to switch live; the next
frame uses the other path. The regular ray-marched path is unchanged and remains
the default; voxel rendering is **off by default** (settings written by older
builds are migrated to off once).

The voxel shaders are deliberately **not** precompiled by the editor's background
shader preload. They are the largest shaders in the engine (the de-voxelization
shader is built with D3DCompiler's O3 pass and can take several minutes), so they
compile lazily on the first frame where the voxel path is actually enabled. A
normal editor session never pays that cost.

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
frame compiles the large de-voxelization shader with D3DCompiler's O3 pass in the
background (the editor stays responsive while it runs); this can take several
minutes once, after which it is read from the shader cache.
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
- **AA Debug** - one dropdown for both methods: FXAA threshold/direction and
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

### Measured Results

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

The exact-SDF raymarcher is compiled in two stages so a cold cache never blocks
the editor. A fast (unoptimized) variant compiles on a worker thread first and is
swapped in after a couple of seconds; the renderer then compiles the optimized
variant in the background and swaps it in when it is ready. The optimized compile
is still slow (about 15 minutes on this machine, and the inlined terrain sampler
makes it slower than the pre-terrain shader), but it no longer freezes the UI.
Both variants are cached on disk, so later launches get the fast variant from the
cache and swap to the optimized one almost immediately. Set
`DIVISION_SDF_SHADER_OD=1` to skip the background optimized pass while iterating.

Cone-edge detection runs in a separate small kernel that writes a mask; the main
SDF kernel reads an SRV copy. That keeps `None`/`FXAA` on their original register
budget. Multi-kernel shaders require DivisionCompute to emit explicit
`register(tN/uN)` slots (the translator change lives in the sibling
`DivisionTranslate/DivisionCompute` checkout this project builds against).

## Unified Reflection/Refraction Denoising

A full GGX microfacet BRDF is only affordable because the image is denoised: the
ray marcher traces one stochastic reflection ray per primary sample (two samples
per pixel by default), and everything that is *not* stochastic - direct light,
shadows, ambient occlusion, albedo textures and refraction - stays exact.

The previous pipeline ran two denoisers back to back over the whole tonemapped
colour: a bilateral "Division" filter and a two-pass A-Trous wavelet filter. Both
used one roughness value for the entire object, both blurred albedo and direct
lighting along with the reflection noise, and neither processed objects that only
had refraction (so rough glass without a reflection component was never frosted).
They are replaced by **one** denoiser that filters only the radiance terms that are
actually noisy, followed by a temporal accumulation pass that removes the
frame-to-frame flicker the old spatial blur used to hide.

### How it works

The exact SDF pass now writes three linear-radiance targets instead of one
tonemapped colour:

- **base** - direct light, ambient and the first-hit surface shading;
- **reflection** - the stochastic first-surface reflection radiance;
- **refraction** - the refracted light.

It also writes a per-pixel material G-buffer: the roughness sampled from the
material's *roughness texture*, metallic, effect flags and the reflection weight.
The resolve pass adds the filtered reflection/refraction back to the base and
applies ACES. Tonemapping moved out of the SDF shader into that resolve, so the
filter always operates on linear HDR radiance; with the denoiser switched off the
resolve reproduces the tonemapped SDF frame (apart from the glass Fresnel fix
described below).

`UnifiedDenoiseShader` merges the two old filters' ideas:

- an edge-aware **A-Trous wavelet cascade** (5x5 B3-spline kernel, progressively
  wider step sizes) supplies the large-scale averaging;
- a **bilateral** spatial/depth/normal weighting supplies the edge preservation,
  plus a hard object-ID stop so neighbouring objects never mix;
- a **roughness similarity** term keeps a roughness texture's smooth and rough
  regions from being averaged together;
- per-pixel roughness selects how many wavelet scales apply. A mirror reflects
  only through the tightest scale (its image stays sharp), a semi-rough surface
  blends in the wider scales, and rough glass reaches the widest;
- a colour-agnostic neighbourhood-mean anchor clamps fireflies *before* the
  luminance edge stop, so a lone bright sample cannot preserve itself by rejecting
  all of its normal neighbours;
- refraction passes use the same depth/normal/roughness stops but no luminance
  stop. That blurs the refracted background into **frosted glass** in screen space,
  without tracing extra refraction rays. Refraction-only materials are filtered
  too, which the old chain never did.

The filter's extra passes reuse a bounded 1/2/4-pixel step instead of doubling past
it, which removes noise without smearing the reflected environment across half the
object. The firefly anchor is computed once in the first pass and carried in the
reflection target's alpha, so later passes do not rescan the neighbourhood.

### Temporal accumulation

A purely spatial filter leaves a small residual that changes every frame because
the stochastic samples are decorrelated per frame; that residual reads as flicker.
The old denoiser hid it by blurring harder. `TemporalDenoiseShader` instead
accumulates the denoised reflection/refraction channels over frames:

- the world position is reconstructed from the depth buffer and reprojected through
  the previous frame's camera (the engine has no motion vectors, but for static
  geometry this reprojection is exact);
- a history sample is rejected unless depth, normal, object ID and roughness all
  match the current pixel, which handles disocclusions, camera cuts and texture
  roughness boundaries;
- the history colour is clamped to the current frame's local min/max, so stale or
  disoccluded content cannot leave a trail;
- the current-frame weight is 0.08 for a static camera (strong accumulation), and
  rises towards 0.5 with reprojected motion so moving edges do not lag.

A single dispatch per frame, two ping-pong history textures per channel and one
previous depth-normal copy implement it. The history is reset on resize, world
rebind, camera switch, render-path switch and between headless test configurations.

### Reflection cutoff (`SDFMaterial`)

`SDFMaterial.reflectionCutoff` (0-1, default **0.65**) is evaluated **per pixel**
from the roughness texture. Pixels above it skip reflection ray tracing entirely
and use a cheap environment approximation (a sky lookup in the mirror direction,
widened toward the normal for very rough lobes), so no raymarch steps are spent on
lobes that a single ray cannot sample usefully. Because the test is per pixel, a
roughness texture only traces - and therefore only denoises - its smoother areas;
the rough texels use the smooth approximation and are left untouched by the filter.

The camera component exposes the matching unified controls: **Enable Denoise**,
**Denoise Strength**, **Denoise Passes** (reflection A-Trous scales),
**Refraction Denoise Passes** (frost blur scales), **Enable Temporal Denoise** and
**Temporal Weight** (lower = more stable, higher = less motion lag).

Two long-standing bugs were fixed as part of the split:

- the glass Fresnel split used `cosTheta` from the *last* reflection bounce rather
  than the primary hit, so a glass surface's reflectivity varied with the
  stochastic ray direction and its "stable" base channel was noisy;
- environment approximations evaluated on secondary reflection bounces were added
  to the base channel, which gave the same symptom on rough surfaces seen in
  reflections.

Both are now evaluated from the primary hit / routed to the reflection channel, so
the base is genuinely noise-free.

### Measured results

`--cli denoisetest` builds a deterministic scene with metal spheres from mirror to
rough, glass spheres from polished to frosted (including one refraction-only
sphere), a metal slab carrying a checkerboard roughness texture (smooth blocks
below the cutoff, rough blocks above it) and diffuse floor/backdrop surfaces. It
renders the same sample sequence with the denoiser off and on, a 32-spp reference,
debug views of the material flags, per-pixel roughness and cutoff mask, and an
8-frame live sequence for temporal stability. All numbers below are 640x360, 2 spp
versus the reference:

| Check | Before (noisy) | After (denoised) | Change |
| --- | ---: | ---: | ---: |
| Metal reflection RMSE, all traced pixels | 0.02503 | 0.01385 | **-45%** |
| Metal reflection RMSE, roughness 0.12-0.6 | 0.04848 | 0.02455 | **-49%** |
| Mirror RMSE, roughness < 0.12 | 0.01133 | 0.01132 | ~0% |
| Metal RMSE, roughness 0.10-0.25 | 0.03562 | 0.02731 | -23% |
| Metal RMSE, roughness 0.25-0.45 | 0.04274 | 0.02736 | -36% |
| Metal RMSE, roughness 0.45-0.70 | 0.05413 | 0.02004 | **-63%** |
| Frosted-glass high-frequency energy | 0.04685 | 0.02535 | **-46%** |
| Refraction-only glass high-frequency energy | 0.05442 | 0.03169 | **-42%** |
| Smooth glass high-frequency energy | 0.02511 | 0.02466 | -2% (stays sharp) |
| Temporal std, secondary pixels (8 live frames) | 0.00749 | 0.00145 | **-81%** |
| Temporal frame-to-frame change | 0.00815 | 0.00151 | **-81%** |
| Ray steps per reflective pixel, cutoff 1.0 -> 0.4 | 0.3126 | 0.2608 | **-17%** |
| Diffuse/sky pixels changed by the filter | - | **0** of 188,316 | bit-identical |
| Rough (above-cutoff) texels changed | - | **0** of 5,883 | not denoised (2-px block edges excluded) |
| NaN/Inf pixels | - | 0 | - |

The denoiser costs about **2 ms/frame at 1280x720** including temporal accumulation:
`--cli bench --width 1280 --height 720` reports about 47.7 ms with it enabled versus
47.0 ms with `--no-denoise` (the exact SDF pass dominates, and run-to-run GPU clock
variance is of the same order as the denoiser cost).

### Command line

```bash
# Full denoise quality gate (writes noisy, denoised, temporal, reference and channel
# PNGs, masks, temporal-std maps and a difference image); exits 1 on any failed check
DivisionEngine.Editor.exe --cli denoisetest --width 640 --height 360 --out artifacts/denoisetest

# More converged reference (slower), skip the gates, or capture a moving-camera series
DivisionEngine.Editor.exe --cli denoisetest --reference-spp 64 --out artifacts/denoisetest64
DivisionEngine.Editor.exe --cli denoisetest --no-gates --out artifacts/denoisetest-images
DivisionEngine.Editor.exe --cli denoisetest --motion --out artifacts/denoisetest-motion

# Compare temporal accumulation with the spatial filter alone
DivisionEngine.Editor.exe --cli denoisetest --no-temporal --out artifacts/denoisetest-spatial

# Render the debug views directly
DivisionEngine.Editor.exe --cli shot --debug-mode 11 --out roughness.png   # per-pixel roughness
DivisionEngine.Editor.exe --cli shot --debug-mode 12 --out flags.png       # R reflection, G refraction, B traced
DivisionEngine.Editor.exe --cli shot --debug-mode 13 --out cutoff.png      # white = approximated, green = traced
DivisionEngine.Editor.exe --cli shot --denoise-channel 1 --out reflection.png
DivisionEngine.Editor.exe --cli shot --no-temporal --out spatial-only.png

# Denoise off (the old noisy path) and the raw SDF pass
DivisionEngine.Editor.exe --cli shot --no-denoise --out noisy.png
DivisionEngine.Editor.exe --cli shot --no-post --out raw-sdf.png
```

### Limitations

- Temporal accumulation reprojects through the camera, not through object motion
  (the engine has no motion vectors). Static geometry is handled exactly, and the
  depth/normal/object/roughness validation plus the local colour clamp stop most
  disocclusion trails, but a fast-moving object or a reflection of one can leave a
  short ghost. Lower the temporal weight, or disable temporal denoising on the
  camera for those shots.
- Checkerboard rendering disables temporal accumulation because the unrendered half
  of the frame has no current depth/material data.
- Twelve full-resolution `float4` denoise targets are allocated: the spatial targets
  (material G-buffer, reflection/refraction originals, two ping-pong pairs) and the
  temporal targets (two history pairs and the previous depth-normal copy). That is
  about 177 MB at 1280x720 and roughly 1.6 GB at 4K.
- The reflection cutoff approximation samples the sky only; it does not account for
  nearby geometry, so a very rough metal surface above the cutoff can look brighter
  than a fully converged ray-traced reference. Lower the cutoff for a closer match
  and raise it for more reflection detail.
- The converged reference traces sharp refraction, while frosted glass deliberately
  blurs the refraction channel in screen space. The quality gate therefore judges
  glass by its frost (high-frequency energy) rather than by pixel error against that
  reference.
- The voxel clipmap path feeds its whole shaded colour through the reflection
  channel (it has no separate reflection/refraction split yet), so it is denoised
  like the old full-colour path rather than with the material-aware split.

## Area Lights

Directional and point lights are joined by **area lights**: a spherical emitter or a
rectangular softbox whose shadows come from the light's own extent instead of a
tunable penumbra width. The implementation follows R. Bán, Cs. Bálint and G. Valasek,
*Area Lights in Signed Distance Function Scenes* (Eurographics 2019 short paper).

Add one with **Add → Area Light**, or by right-clicking in the World window and picking
**Lights → Area Light** (the World window menu groups every entity type into a submenu:
Lights, Scene, SDF Primitives, SDF Fractals and Audio). Edit it in the Properties window
like any other component:

- **Shape** - *Sphere* (omnidirectional) or *Rectangle* (a softbox that emits from the
  side its local -Z axis faces away from: rotate the entity so -Z points at the
  subject, exactly like a directional light).
- **Intensity** / **Color** - the same convention as a point light: intensity is the
  irradiance the light delivers at one world unit. An area light therefore replaces a
  point light one-for-one and only changes the shadow, and the emitter's size never
  changes the overall brightness of the scene.
- **Radius** (sphere) or **Size** (rectangle) - the emitter's physical extent. This is
  what sets the penumbra width, and the penumbra hardens towards the contact point the
  way a real light does.
- **Range** - attenuation cutoff, 0 disables it.
- **Samples** (rectangle) - light samples per pixel for the numerical integration.
- **Epsilon** - the paper's cone-search accuracy parameter; smaller is more accurate and
  slower, and larger lights want smaller values.

### How the shadows are computed

The paper's *minimal tangent cone* search (its Algorithm 2) is what makes the shadow
cheap: a cone is constructed from the shaded point towards the light's bounding sphere,
and sphere tracing along the cone axis shrinks the cone around the closest surface until
the relative cone radius `m` stops decreasing. `m = 1` means nothing occludes the cone and
`m <= 0` means the surface crosses the axis. Each step uses the Accelerating Sphere
Tracing step of Bálint and Valasek, `(f - r - t tan a) / (1 + tan a)`, and every time the
cone touches a surface it is rescaled to `m - epsilon`, which pockets the occluder and
lets the march take much longer steps than a shadow ray of the same radius would.

The two shading paths then use that single search result:

- **Spherical lights** use the paper's closed form for the visibility term (Section 4.1).
  The reported `m` is mapped through `smoothstep(0, 1, m/2 + 1/2)`, which is identically
  `((m/2 + 1/2)^2 * (2 - m))`, the paper's fast stand-in for the exact circular integral
  `m/pi * sqrt(1-m^2) + asin(m)/pi + 1/2`. That visibility factor multiplies the light's
  ordinary punctual contribution, which is the paper's Equation 2 factored into BRDF
  times visibility times irradiance.
- **Rectangular lights** integrate Equation 1 numerically (Section 4.2). The largest
  occluder found by the cone search becomes a tangent plane through its closest point, so
  each light sample costs one dot product to be classified as visible or hidden instead of
  another shadow trace, while the light's own shape is tested exactly. The paper samples
  directions inside the cone and tests them against the rectangle; this implementation
  samples the rectangle's area, which integrates the same estimate with far less variance
  at the sample counts an editor frame can afford.

Neither path touches the existing shadow styles: `CalculateShadow`, the penumbra ("soft")
shadow and the coloured glass shadow keep their own code untouched, and point and
directional lights are unaffected by area light support.

### Measured results

`--cli arealighttest` renders the same scene through the area light path and through
independent references, so these numbers are not golden images:

| Check | Result |
| --- | --- |
| Sphere light visibility vs a 1024-sample Monte Carlo integration of the exact visibility (radius 0.5 / 1.5 / 3 / 5) | mean error **0.04% / 0.08% / 0.22% / 1.10%**, correlation 0.9999 / 0.9999 / 0.9996 / 0.9976 |
| The same scene, the existing point-light penumbra shadow with a matched penumbra width | mean error **13.8%** (the paper's Quilez comparison) |
| Rectangle light vs a CPU mirror of the paper's integration (64 spp, tilted emitter) | mean relative error **0.41%**, RMS **0.76%**, median rendered/expected exactly **1.000** |
| Rectangle light shrunk to a point vs a point light of equal intensity and colour, straight under the emitter | **0.38%** |
| Four-light rig (directional + point + sphere + rectangle) vs the sum of the four, 174,480 pixels | **0.00%** RMS, **0** NaN pixels |
| Rectangle light sample convergence (RMS vs 64 spp) | 1 spp 0.0053, 4 spp 0.0008, 16 spp 0.0002 |

(Rendered at 640x360 with the fast, unoptimized shader variant, so the numbers do not
depend on a tuned build.)

The sphere light's shadow therefore lands within about 1% of the exact visibility, while
the engine's existing penumbra approximation of the same scene is off by 14% - the
paper's headline result. The rectangle light matches the paper's integral to well under
a percent, and area lights mix with the existing light types linearly and without
double counting.

### Limitations

- Rectangular emitters evaluate Equation 1 with `Samples` samples per pixel, so their
  penumbrae are dithered rather than smooth at low counts - the trade-off the paper
  reports for its second algorithm. Sphere lights are closed form and have no sampling
  noise at all. Raise **Samples** for hero renders; 16 is the default and 32-64 removes
  visible dithering from a large softbox.
- The paper's edge-like occluder assumption is weakest for a very large emitter close to
  a receiver and inside concave corners, which is exactly the accuracy the paper's
  Section 5 reports.
- The voxel clipmap path approximates area lights as point lights at the emitter centre:
  it keeps the brightness convention of the exact path but not the soft shadow, in the
  same way it simplifies the rest of its lighting model.
- The area light cone search costs the same order as one shadow ray per light, and a
  rectangle light additionally evaluates the BRDF once per sample.

### Command line

```bash
# Full area light suite: sphere light shadows against a Monte Carlo ground truth,
# rectangle lights against a CPU mirror of the paper's integration, and additivity of a
# combined directional + point + sphere + rectangle rig
DivisionEngine.Editor.exe --cli arealighttest

# One group only, writing its images to a directory
DivisionEngine.Editor.exe --cli arealighttest --scene sphere --out-dir artifacts/area

# The structured-buffer layout of SDFLightDTO, area light fields included
DivisionEngine.Editor.exe --cli compute selftest
```

## Terrain System

Terrains are height fields evaluated directly inside the SDF raymarcher. The terrain
solid's surface is `y = height(x, z)`, so it has no triangle edges, its normals come
from the analytic gradient, and it can be intersected with every other SDF in the
scene. A terrain sample stores height **and** both slope components, which is what
makes the cubic reconstruction and the shading cheap.

### Procedural Erosion

The base height is a three-octave gradient-noise FBM (the `noised` field also returns
analytic derivatives). On top of it, the terrain applies **Rune Skovbo Johansen's
"Advanced Terrain Erosion Filter"** — the stacked faded gully technique from
[the Shadertoy](https://www.shadertoy.com/view/wXcfWn) and
[the video](https://www.youtube.com/watch?v=r4V21_uUK8Y)
([blog post](https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html)).
It layers directional Phacelle-noise gullies over the slope field, octave by octave,
and masks each new octave by the previous one, so the result has branching drainage
networks, crisp interlocking ridges and creases, and a ridge map that can be used for
drainage/vegetation shading. Only Rune's scale-independent parameters are exposed
(erosion strength, gully weight, detail, scale, octaves, lacunarity, gain, cell scale,
normalization).

The technique is a *filter*, not a simulation: every point is evaluated in isolation,
so it is trivially parallel and chunked, which is exactly what the infinite terrain
needs. The filter is MPL-2.0 licensed; the attribution lives at the top of
`DivisionEngine.Core/Rendering/Terrain/TerrainRendering.cs`. It derives from Clay
John's and Fewes' earlier Shadertoys.

### Heightmap Reconstruction

Each texel stores `(height, dH/dx, dH/dz, ridge)`. `SDFTerrain.interpolation`
selects how those samples are reconstructed:

- **Hermite** (default) uses the tensor-product cubic Hermite patch from R. Bán and
  G. Valasek, *Hermite interpolation of heightmaps* (Eurographics 2022 poster).
  The stored analytic slopes are the Hermite tangents, so a sample reads only four
  texels while producing a C1 surface with exact analytic normals and smooth
  silhouettes.
- **Bilinear** uses the bilinear patch itself for both height and shading. Its
  normal is the **exact gradient of the bilinear reconstruction**, not the stored
  analytic slopes: the slopes describe the pre-filtered source surface, so reusing
  them would light a deliberately faceted terrain as if it were smooth. The result
  is the expected flat, rectangular look.

Reconstruction error against the source function (mean absolute height error over a
sweep of random sample positions): bilinear **0.0897**, cubic Hermite **0.0048**,
Catmull-Rom **0.0071**. Hermite is ~19x more accurate than bilinear at half the fetch
footprint of the 16-texel Catmull-Rom kernel.

### Fixed and Infinite Terrains

`SDFTerrain.infinite` selects the mode:

- **Fixed** (`infinite = false`) bakes one heightmap on the GPU whenever the component
  changes. The height field is clipped to a square prism, so the sides read as a
  layered rock cross-section instead of an infinitely thin sheet. The map resolution
  and filtering mode are configurable, so a 512-texel map with cubic reconstruction
  is smooth where a 2048-texel bilinear map used to show facets.
- **Infinite** (`infinite = true`) streams a quadtree cut of heightmap chunks around
  the camera the render window is actually showing: the free-flying **editor camera**
  while editing (and while a play session is paused, when the editor camera is
  re-activated), and the **gameplay (main) camera** during play mode or in a player
  build. Each LOD level is a square grid of world-aligned chunks; a chunk always
  occupies the same storage slot, so moving the camera only replaces the chunks that
  enter the grid (no buffer shifting). Every slot carries the identity of the chunk it
  holds, so a stale slot is detected and never sampled. The LOD ring is chosen from
  camera distance with power-of-two chunk sizes.

Chunks are baked on background worker threads (`bakeThreads`) and uploaded at most
`chunksPerFrame` per frame through ranged `UpdateSubresource` patches, so streaming
never rebuilds the whole heightmap buffer. A chunk that has not arrived yet falls back
to the next coarser level; outside every loaded ring a flat sea-level plane keeps the
world solid, and the outermost ring fades to it so the clipmap never ends in a cliff.
The fade is a circle centred on the same camera-following LOD anchor, and the GPU
metadata is refreshed every frame, so the fade recenters as the camera moves rather
than staying at its initial position. The same fade applies to both interpolation
modes.

Terrain also participates in **its own shadows and ambient occlusion**. The engine's
AO approximates occlusion with the second-closest object, which by design cannot
occlude an object with itself; a height field needs self-occlusion most of all. The
terrain therefore marches its own SDF in the shadow ray (with a larger ray bias to
avoid acne) and derives an ambient self-occlusion term from the baked ridge map
(creases shadowed, ridges exposed). Without these two terms eroded mountains read as
flat and washed out regardless of the heightmap detail.

On this machine the full 200-chunk quadtree (8 levels, 24 m level-0 chunks, 80-texel
chunks) streams in about **1.4 s** on four bake threads, and the CPU and GPU heightmap
bakes agree to within **0.001 m**. The GPU-side layout and LOD fallback are exercised
by a CPU mirror of the sampler in the terrain tests.

### CLI

```bash
# Height-field/erosion reference images and a ray-marched preview (no GPU needed)
DivisionEngine.Editor.exe --cli terrain image --out terrain-images
DivisionEngine.Editor.exe --cli terrain preview --out terrain.png --grid 1024

# Unit checks: height bounds, Hermite accuracy/continuity, chunk seams,
# quadtree slot stability, buffer sampling and LOD fallback
DivisionEngine.Editor.exe --cli terrain unit

# Compare the GPU bake shader against the CPU bake
DivisionEngine.Editor.exe --cli terrain gputest --chunk 64

# CPU bake throughput
DivisionEngine.Editor.exe --cli terrain bench --chunk 96

# Deterministic terrain capture (fixed or streaming quadtree). Infinite captures
# pre-warm the quadtree first so screenshots are fully streamed; pass --no-prewarm
# to capture mid-streaming instead. --diagnostics prints the GPU metadata, chunk
# table and sampled heights.
DivisionEngine.Editor.exe --cli terrain shot --fixed-frame 3 --out terrain-fixed.png
DivisionEngine.Editor.exe --cli terrain shot --infinite --fixed-frame 3 --out terrain-infinite.png
# Compare the two reconstruction modes on a deliberately coarse heightmap
DivisionEngine.Editor.exe --cli terrain shot --interpolation bilinear --heightmap-size 32 --out terrain-bilinear.png
DivisionEngine.Editor.exe --cli terrain shot --interpolation hermite --heightmap-size 32 --out terrain-hermite.png
```

Measured unit-test results: all 14 terrain checks pass; bilinear reconstruction
error 0.0897 vs cubic Hermite 0.0048 and Catmull-Rom 0.0071; adjacent chunk seams
match exactly; the GPU bake differs from the CPU bake by at most 0.00095 m; the
sampled fixed terrain matches the analytic height field to 0.000 m (mean) with no
missing samples.

Terrains are exposed through the **SDFTerrain** component (bounds, height, frequency,
filtering, erosion and quadtree settings) and appear in the Add menu as
**Terrain**. The default world ships a fixed terrain; create a new terrain and enable
**Infinite** to stream one that follows the camera.

### Limitations

- Infinite terrain LOD rings currently switch levels without geometric blending, so a
  very distant ring boundary can show a small step in the silhouette. The LOD distance
  also includes the camera's height above the terrain, so an aerial camera selects a
  coarser ring than its XZ offset alone would suggest.
- At grazing aerial angles the finest LOD rings can show fine comb-like shading in the
  reconstructed surface. The Hermite patch itself is C1 (including the Z-slope field,
  which is interpolated with the Hermite value basis in X); the remaining combing is
  most visible where LOD rings meet and is still being investigated.
- The infinite quadtree is a camera-centred cut with fixed power-of-two rings rather
  than a per-node screen-space error metric; occlusion culling and a sparse node set
  are future work.
- Terrain overhangs and caves are not supported by a height field; the planned 3D
  overhang work in the Trello board would need an additional volume layer.
- Non-uniform terrain scaling and pitch/roll rotation are not handled specially; use
  uniform scale and yaw-only rotation.
- The terrain sampler is inlined into the raymarcher, so the optimized SDF shader
  takes several minutes to compile. The renderer compiles a fast variant on a worker
  thread first and renders immediately, then compiles the optimized variant in the
  background and swaps it in when ready; both are cached afterwards. Set
  `DIVISION_SDF_SHADER_OD=1` to skip the background optimized pass while iterating.
- Self-shadowing and self-occlusion are terrain-specific; other objects still use the
  engine's existing second-object AO approximation and do not self-shadow.

### What About the Planned Marketplace?

In the future there will be a Division Engine marketplace, completely separate with its own terms. The engine remains free and GPL-licensed. The marketplace will be where creators can buy/sell assets, projects, etc. with revenue supporting ongoing development.

**In short:** You can build anything with Division Engine - free games, commercial games, modded versions - as long as any distributed version (including modified versions) remains under GPL 3.0 with source code available.
*Full license terms are available in [LICENSE.txt](LICENSE.txt)*
