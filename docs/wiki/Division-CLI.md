# Division CLI

The Division CLI is the headless command surface of Division Engine. It runs the same
render pipeline, shaders, and world construction as the editor, but never opens the
editor window — which makes it usable from scripts, batch files, and CI.

It is implemented in `DivisionEngine/DivisionCLI.cs`, with the compute commands in
`DivisionEngine/CLI/DivisionComputeCLI.cs`.

## Invocation

```shell
DivisionEngine.Editor.exe --cli <command> [options]
```

From a source checkout:

```shell
dotnet run --project DivisionEngine -- --cli <command> [options]
```

`--cli` must be the **first** argument (case-insensitive). Without it the editor starts
normally. The executable is a GUI-subsystem app (`WinExe`); it calls `AttachConsole` to
write to the terminal that launched it, so run it from a terminal rather than by
double-clicking it.

Running `--cli` with no command, or with `help`, prints the command list.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Command completed successfully |
| `1` | Unknown command, invalid/missing option, or a command-level failure (unhandled exception, image comparison over tolerance) |

Unhandled exceptions are printed as `divisioncli: <exception>` and exit `1`, so a
non-zero exit is always safe to gate a build on.

## Commands

| Command | Purpose |
| --- | --- |
| `shot` | Render one frame to a PNG |
| `bench` | Render frames offscreen and report frame times |
| `compare` | Compare two PNGs per channel (the golden-image gate) |
| `compute` | DivisionCompute shader tooling — see [DivisionCompute CLI](Division-Compute-CLI) |
| `info` | Print engine and runtime information |
| `selftest` | Run headless regression checks (exits `1` on failure) |
| `skyprobe` | Print sun/atmosphere values for the default world |
| `help` | Print usage |

---

## `shot`

Renders one frame of the default world to a PNG.

| Option | Default | Meaning |
| --- | --- | --- |
| `--out <path>` | `shot.png` (current directory) | Output file |
| `--width N` | `1280` | Image width |
| `--height N` | `720` | Image height |
| `--sky <mode>` | `gradient` | `solid`, `gradient`, `hdri`, `procedural` |
| `--look x,y,z` | camera default | Aim the first camera at a world-space direction |
| `--debug-mode N` | `0` | `0` shaded, `1` depth, … `9` mip levels |
| `--fixed-frame N` | off | Deterministic capture: render exactly `N` frames (see below) |
| `--seed N` | `12345` | World RNG seed for deterministic capture |
| `--no-checkerboard` | off | Disable temporal jitter |
| `--reload-shaders` | off | Drop compiled shaders halfway through a `--fixed-frame` capture |
| `--no-post` | off | Disable every post-processing stage |
| `--no-denoise` | off | Disable the denoisers only |
| `--no-fxaa` | off | Disable FXAA only |
| `--effects` | off | Enable a fixed post chain (blur, vignette, chromatic aberration, hue shift, saturation) |
| `--bloom` | off | Include bloom; only applies together with `--effects` |

Output is the PNG plus a `stats` line (`minLum`, `maxLum`, `meanLum`, `nonBlack`, `nan`,
`debugMode`), which makes an all-black or NaN frame obvious in a headless run.

### Deterministic capture

`--fixed-frame N` renders exactly `N` frames at a fixed `1/60 s` step with the
checkerboard (temporal) path disabled and the world RNG seeded, so two runs produce
identical pixels. With it enabled, the `shot` line also prints:

```
shot: out.png (... fixedFrame=8, seed=12345, deterministic, frameCount=..., time=...,
      sdfObjects=..., worldHash=<hash>, objectHash=<hash>)
```

`worldHash` and `objectHash` are FNV-1a 64 hashes of the scene data uploaded to the GPU.
Two captures with the same hashes were provably fed the same scene — useful when a pixel
diff needs to be blamed on the renderer rather than on the scene.

`--reload-shaders` splits a deterministic capture in half and drops the compiled shader
cache between them, which tests "Edit → Reload All Shaders" without opening the editor.

---

## `bench`

Renders frames offscreen and reports frame times. No files are written.

| Option | Default | Meaning |
| --- | --- | --- |
| `--width N` | `1280` | Render width |
| `--height N` | `720` | Render height |
| `--frames N` | `120` | Timed frames |
| `--warmup N` | `10` | Untimed warmup frames |
| `--sky <mode>` | `default` | `default` or `procedural` |

It accepts the same render switches as `shot` (`--effects`, `--bloom`, `--no-post`,
`--no-denoise`, `--no-fxaa`), and prints one JSON object:

```json
{"width":1280,"height":720,"frames":120,"avgMs":36.5,"medianMs":36.1,"p95Ms":38.4,"fps":27.4}
```

`width`, `height`, and `frames` must be positive.

---

## `compare`

Compares two PNG renders per channel and fails the process when any pixel exceeds the
tolerance. This is the golden-image gate.

| Option | Default | Required | Meaning |
| --- | --- | --- | --- |
| `--a <golden.png>` | — | yes | Reference image |
| `--b <candidate.png>` | — | yes | Image under test |
| `--tolerance N` | `2` | no | Allowed per-channel difference, in 8-bit LSBs |
| `--diff <path>` | off | no | Write an amplified difference image |

```shell
DivisionEngine.Editor.exe --cli compare --a golden.png --b shot.png --tolerance 2 --diff diff.png
```

Prints a JSON summary (`maxDiff`, `badPixels`, `over8`, `over32`, `meanDiff`,
`totalPixels`) and exits `1` when `badPixels > 0`. Images of different sizes are an
error. The `artifacts/goldens/` tree is gitignored, so local capture notes there (capture
commands, expected per-build deltas) only exist in a working copy — see
`artifacts/goldens/README.md` if it is present.

---

## `info`

Prints the .NET runtime version, OS version, and whether a project is loaded:

```
Division Engine CLI
Runtime: 10.0.14
OS: Microsoft Windows NT 10.0.26200.0
Project loaded: False
```

Useful as the first line of a CI log to record what the machine actually ran.

---

## `skyprobe`

Builds the default world and dumps the environment and sun values it would render with:
`skyType`, `skyIntensity`, `sunIntensity`, `sunColor`, `sunDiscSize`, `sunFalloff`,
`planetRadius`, `atmosphereHeight`, `atmosphereDensity`, `atmosphereExposure`, plus the
directional light's main light direction. No files are written.

---

## `selftest`

Runs headless regression checks for behaviour that has no visible surface, and exits `1` if
any check fails. No GPU, window, or project is required, so it is safe to run first in CI:

```shell
DivisionEngine.Editor.exe --cli selftest
```

It covers locale-independent component serialization (`float2/3/4`, `float4x4` written and
read on a comma-decimal culture), the DivisionMath matrix accessors, world snapshot/restore
(entity IDs and the editor camera), undo-stack trimming and edit coalescing, the play-mode
single-step gate, and the terrain noise range. Output is one `PASS`/`FAIL` line per check,
a JSON summary (`{"checks":N,"passed":N,"failed":0}`), and a final `selftest: PASS|FAIL`.

---

## Typical golden-image loop

```shell
# 1. Capture a reference from a known-good build
DivisionEngine.Editor.exe --cli shot --fixed-frame 8 --width 1280 --height 720 --out golden.png

# 2. After a change, capture the same configuration
DivisionEngine.Editor.exe --cli shot --fixed-frame 8 --width 1280 --height 720 --out shot.png

# 3. Gate on it (exit code 1 when the tolerance is exceeded)
DivisionEngine.Editor.exe --cli compare --a golden.png --b shot.png --tolerance 2 --diff diff.png
```

Because `--fixed-frame` also seeds the world RNG, step 2 is reproducible across machines
for the same build. Same build, same driver, same result: `--tolerance 0` is achievable.

## Gotchas

* `--cli` must come first; `DivisionEngine.Editor.exe shot` opens the editor.
* Option parsing is `--name value` and flag-based; there is no `-w` shorthand and no
  `--name=value` form.
* `--bloom` does nothing unless `--effects` is also present.
* `compare` needs two PNGs of identical dimensions; capture both at the same
  `--width`/`--height`.
* Missing numeric values silently fall back to the default (for example `--width abc`
  renders at 1280), so check the `stats`/JSON line rather than trusting the arguments.
