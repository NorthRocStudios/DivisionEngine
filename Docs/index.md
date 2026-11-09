---
title: Division Engine Documentation
---

# Division Engine

Division Engine is an SDF-based game engine written entirely in C#, using Avalonia UI
for the interface, Silk.NET for native rendering and OpenAL for audio. Shaders are
written in C# with ComputeSharp (migrating to DivisionTranslate), so there is no
separate HLSL authoring step.

> **This site is generated from the source code.** Every page under
> [API Reference](api/DivisionEngine.yml) is produced by DocFX from the XML doc comments in the
> repository, and is rebuilt automatically whenever the `main` branch changes.
> The articles are Markdown files under `Docs/articles/`.

## Start here

| Section | What it covers |
| --- | --- |
| [Getting Started](articles/getting-started.md) | Building and running the editor and player |
| [API Reference](api/DivisionEngine.yml) | Every public type, generated from source |
| [ECSs and worlds](articles/getting-started.md#ecs) | Entities, components, systems and the world loop |

## Useful entry points

- <xref:DivisionEngine.World> - entity/component storage and the system schedule
- <xref:DivisionEngine.SystemBase> - base class for all engine systems
- <xref:DivisionEngine.Components.Transform> - position, rotation and scale
- <xref:DivisionEngine.Audio.AudioEngine> - audio playback and spectrum analysis
- <xref:DivisionEngine.Rendering.RenderPipeline> - the SDF render pipeline

## Contributing to the docs

Good documentation starts in the code: give public types and members useful
`///` summaries and they will appear here on the next build. Longer guides live in
`Docs/articles/` as Markdown and are picked up automatically.

