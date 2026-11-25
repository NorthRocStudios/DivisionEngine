# How Division Engine is built

**Short version:** written by one person, with an AI coding assistant helping, against a
real regression and benchmark suite. This page exists so nobody has to guess which parts
are which.

## The disclosure, plainly

Division Engine is developed with **DeepSeek Harness**, an AI coding assistant, used on
purpose and without embarrassment. It is a tool in the same category as a compiler, a
debugger or an autocomplete engine — it does not decide what the engine should be, and it
does not know when the output is wrong. That job is still mine.

If you are here to decide whether the project is real: the code is open, the history is
public, the installers are downloadable, and every rendering feature ships with a scene
that can be re-rendered and compared. Judge the artifacts, not the tooling.

## What the assistant is actually used for

- **Bug hunting.** Rotation and matrix sign errors, misspellings in editor strings,
  null paths in the asset pipeline.
- **Editor tooling.** Boilerplate view-models, serialization plumbing, the parts of an
  editor that are necessary, repetitive and boring.
- **Paper implementation drafts.** Turning a described algorithm into a first-pass
  implementation that then gets corrected against the actual results.
- **Refactors.** The ComputeSharp → DivisionCompute/DivisionMath migration, for example.

**Rule of thumb:** anything that has a right and wrong answer, verified by a build or a
render, is fair game. Anything that requires taste — what the renderer should look like,
which tradeoff is worth it, which feature matters next — is decided by hand.

## What "verified" means here

There is no meaningful unit-test suite. Instead the engine has a headless CLI that renders
fixed scenes to disk, and those scenes are the regression tests:

| Command | Scene |
|---|---|
| `--cli selftest` | broad engine self-check |
| `--cli ctsstest` | cone-traced supersampling (SDF edge AA) |
| `--cli denoisetest` | reflection/refraction spatial + temporal denoiser |
| `--cli arealighttest` | sphere/rectangle emitters, soft shadows |
| `--cli terraintest` | terrain bake, erosion, streaming and LOD |
| `--cli voxeltest` | voxel SDF clipmap |
| `--cli shadowtest` | shadow paths and caustics |
| `--cli nodetest` | node graph evaluation |
| `--cli bench` / `--cli compare` | timings and reference-image diffing |

Outputs land in `artifacts/`: reference images, diff images, and per-scene timing logs.
That folder is the receipt. If a feature claim on social media looks better than the
screenshot in the repo, believe the repo.

## The numbers

- **474 commits** over **15 months**, single author.
- **Three alpha releases**, all with downloadable Windows installers.
- **Seven wiki pages** of feature documentation.
- Every rendering feature above was implemented, then re-rendered, then corrected.

## Why keep it open about this

Because the alternative is a project that quietly looks like more than it is. The engine
is genuinely behind: no custom model import, no standalone player export, 27–35 fps at
720p in a scene with terrain, refraction, reflection and post-processing. Those limits are
real and documented in the README.

The honest claim is narrow: **the techniques in here are implemented properly and you can
check them yourself.** That's the whole pitch.

## How to verify any of this

```powershell
git clone https://github.com/NorthRocStudios/DivisionEngine
cd DivisionEngine
dotnet build DivisionEngine.sln
DivisionEngine.Editor.exe --cli help
DivisionEngine.Editor.exe --cli selftest
```

Then read `artifacts/`, and compare against whatever claim you came here to check.

## License

MIT, except the terrain erosion filter portions of
`DivisionEngine.Core/Rendering/Terrain/TerrainRendering.cs`, which remain MPL-2.0. See
[LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
