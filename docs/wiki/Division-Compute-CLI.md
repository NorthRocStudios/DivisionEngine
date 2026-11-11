# DivisionCompute CLI

Shader tooling exposed through the [Division CLI](Division-CLI). It discovers every
`[Shader]` struct in the assembly, translates the C# kernels to HLSL, compiles them, and
can compare GPU output against CPU reference implementations.

Implemented in `DivisionEngine/CLI/DivisionComputeCLI.cs`; entry point
`DivisionEngine/CLI/ComputeProbes.cs`.

## Invocation

```shell
DivisionEngine.Editor.exe --cli compute <command> [options]
```

| Command | Purpose |
| --- | --- |
| `info` | Device, adapter, and shader inventory |
| `list` | List shader structs, kernels, and resources |
| `compile` | Translate and compile every shader; fails on any error |
| `selftest` | Run probe shaders against CPU references |
| `help` | Print usage |

All commands exit `1` on failure (`compile`: any shader fails; `selftest`: any probe
fails).

---

## `compute info`

Prints the DivisionCompute and DivisionMath versions, the D3D adapter, vendor/device IDs,
dedicated VRAM, feature level, the device-removed reason, and every discovered shader
struct. Good as a CI environment record — a device-removed reason or an unexpected
software adapter explains most mysterious shader failures.

---

## `compute list`

| Option | Default | Meaning |
| --- | --- | --- |
| `--filter <text>` | none | Case-insensitive substring match on the shader type name or full name |

Each entry prints the shader's source file, its kernels with thread-group dimensions
(`Name(x,y,z)`), and its resources. Ends with a JSON line: `{"shaders":N}`.

---

## `compute compile`

Translates and compiles shaders in parallel (except with `--print-hlsl`, which forces a
serial run so the output stays readable).

| Option | Meaning |
| --- | --- |
| `--filter <text>` | Only compile shaders matching the substring |
| `--hlsl <dir>` | Write the generated HLSL as `<ShaderName>.hlsl` into `dir` |
| `--cache-dir <dir>` | Compile into `dir` instead of the default user cache |
| `--print-hlsl` | Print the generated HLSL for each shader |
| `--translate-only` | Translate to HLSL but skip `fxc` compilation |
| `--skip-opt` | Compile with optimizations disabled (`-Od`) |
| `--opt1` | Compile at optimization level 1 instead of the default |
| `--verbose` | Print bytecode sizes and extra detail |

Summary line:

```json
{"shaders":23,"compiled":23,"failed":0,"kernels":23,"bytes":123456}
```

Exit code is `0` only when `failed` is `0`.

### Shader cache

Compiled bytecode is cached under `%LOCALAPPDATA%\DivisionEngine\ShaderCache`. Set
`DIVISION_DISABLE_SHADER_CACHE=1` to always recompile — do this in CI when you want the
run to prove that the current source compiles, rather than that the cache is warm.

### Optimization flags matter

The renderer is a raymarcher over high-frequency geometry, so `fxc` optimization level
changes pixels: compiling identical HLSL at `-Od` versus `-O3` produces a larger delta
than most real code changes. When comparing images across builds, keep the optimization
flags identical (see `artifacts/goldens/README.md`).

---

## `compute selftest`

Runs probe shaders and compares their GPU output with a CPU reference implementation on
the same input, at `256×192` for every probe.

| Option | Default | Meaning |
| --- | --- | --- |
| `--iterations N` | `20` | Timed iterations per probe (minimum 1) |
| `--out <dir>` | off | Write `expected` / `actual` / `diff` PNGs per probe |
| `--verbose` | off | Extra shader diagnostics |

Output is a table followed by a JSON summary:

```
probe        result  max error    mean error   mismatch    gpu ms     cpu ms
copy         PASS      0.00E+000     0.00E+000         0     0.412      0.003
...
{"probes":9,"passed":9,"failed":0}
```

Exit code is `0` only when every probe passes. A failure here means the translated HLSL
disagrees with the C# reference — inspect the `--out` images and the reported max error
before blaming the renderer.

---

## Typical CI sequence

```shell
set DIVISION_DISABLE_SHADER_CACHE=1

DivisionEngine.Editor.exe --cli compute info
DivisionEngine.Editor.exe --cli compute compile
DivisionEngine.Editor.exe --cli compute selftest

DivisionEngine.Editor.exe --cli shot --fixed-frame 8 --width 1280 --height 720 --out shot.png
DivisionEngine.Editor.exe --cli compare --a artifacts/goldens/default_1280x720.png --b shot.png --tolerance 2
```
