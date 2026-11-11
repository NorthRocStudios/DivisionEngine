# Division Engine Wiki

Documentation for Division Engine that does not belong in the README: command-line
tooling, render verification workflows, and (over time) architecture notes for the
SDF renderer, ECS, and asset pipeline.

## Start here

| Page | What it covers |
| --- | --- |
| [Division CLI](Division-CLI) | `shot`, `bench`, `compare`, `info`, `skyprobe` — headless rendering and golden-image gates |
| [DivisionCompute CLI](Division-Compute-CLI) | `compute info`, `list`, `compile`, `selftest` — shader discovery, compilation, GPU/CPU conformance |

## Quick start

Render a deterministic frame from a normal terminal:

```shell
DivisionEngine.Editor.exe --cli shot --fixed-frame 8 --width 1280 --height 720 --out shot.png
```

Every command runs the real render pipeline without opening the editor window, so the
same commands work in a `.bat` file, a PowerShell script, or CI.

## How this wiki is maintained

* Pages are plain Markdown, one file per page.
* `Home.md` is this page, `_Sidebar.md` is the navigation, `_Footer.md` (if present)
  appears at the bottom of every page.
* Content is written against the source tree; if a command changes in
  `DivisionEngine/DivisionCLI.cs` or `DivisionEngine/CLI/DivisionComputeCLI.cs`, the
  matching page must change in the same release.
* See `docs/wiki/README.md` in the main repository for the publishing workflow.

Need something documented that is not here yet? Open an issue on the repository and
name the page it should live on.
