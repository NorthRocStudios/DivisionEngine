<div align="left">

<img width="128" height="128" alt="DivisionEngineLogoR" src="https://github.com/user-attachments/assets/4495b5e3-012d-42e4-8e04-1eecbf22ee1d" />

# Division Engine

[![Version](https://img.shields.io/github/v/release/DivisionEngine/DivisionEngine?label=Version&include_prereleases)](https://github.com/DivisionEngine/DivisionEngine/releases)
[![License](https://img.shields.io/github/license/DivisionEngine/DivisionEngine)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![AvaloniaUI](https://img.shields.io/badge/Avalonia-UI-blue)](https://avaloniaui.net/)
[![Sponsor](https://img.shields.io/badge/Sponsor-GitHub%20Sponsors-ea4aaa?style=flat&logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Rex-J-W)

</div>

Division Engine is an SDF-based game engine written entirely in C#. Utilizing Avalonia UI for the interface and Silk.NET for native rendering, Division Engine features a comprehensive build pipeline that dynamically builds HLSL shader code from .NET code, thanks to a library called ComputeSharp.

*Note: This engine is still in preview and has known issues; it is specifically built for experimentation and education only.*

The render pipeline is built using an OpenGL backend with HLSL shaders written in C# using ComputeSharp.

Picture this:
- SDF-based rendering
- GPU compute acceleration in C#
- Open source
- ECS backend, fast data handling
- Convenient editor tooling

## What Are SDFs?

*Signed Distance Fields* are spatial fields that store information represented as a grid sampling of the closest distance to the surface of an object defined as a polygonal model. Usually, the convention of using negative values inside the object and positive values outside the object is applied. Signed distance fields are important in computer graphics and related fields. Often, they are used for collision detection in cloth animation, soft-body physics effects, malleable geometry, volumetric effects, and fluid simulation.
(https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-34-signed-distance-fields-using-single-pass-gpu)

## How to Work with ECS

*ECS* or an entity-component-system framework is a way of organizing game data such that it is memory efficient and hyper-performant. Entities are simply IDs with components stored as a dictionary in an "ECS World" object. Systems are code files written that operate on an awake --> update --> fixed update --> render schedule, allowing components to be manipulated during different engine loops/stages. For more information on ECS, check out how the Unity game engine implemented its ECS framework here: https://unity.com/ecs

## Framework

Division Engine is built using three core packages: Silk.NET, ComputeSharp, and AvaloniaUI.
Check them out here:
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [ComputeSharp](https://github.com/Sergio0694/ComputeSharp)
- [AvaloniaUI](https://github.com/AvaloniaUI/Avalonia)

> **Upcoming Change:** In a future release, **ComputeSharp will be replaced by [DivisionTranslate](https://github.com/NorthRocStudios/DivisionTranslate) and [DivisionMath](https://github.com/NorthRocStudios/DivisionMath)**.  
> DivisionTranslate will provide a shader compiler and translator, while DivisionMath will offer a unified math API. Together, they will deliver the full functionality of ComputeSharp and more with improved cross-platform support, CPU/GPU parity, and a consistent math layer for both CPU and GPU code.

## Resources:
Follow the development: https://trello.com/b/mWtyHBMf/division-engine

Tutorials by Inigo Quilez (Not sponsored, just useful for learning constructive geometry):
- Build mathematical worlds: https://youtu.be/0ifChJ0nJfM?si=ypKU1rz-8JloPlj2
- Build a 3D landscape: https://youtu.be/BFld4EBO2RE?si=EASXvq-ez2qBOIHN
- Paint a 3D character with math: https://youtu.be/8--5LwHRhjk?si=fH9QwvCz6dLptHE1

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

### What about the planned marketplace?

In the future there will be a Division Engine marketplace, completely separate with its own terms. The engine remains free and GPL-licensed. The marketplace will be where creators can buy/sell assets, projects, etc. with revenue supporting ongoing development.

**In short:** You can build anything with Division Engine - free games, commercial games, modded versions - as long as any distributed version (including modified versions) remains under GPL 3.0 with source code available.
*Full license terms are available in [LICENSE.txt](LICENSE.txt)*

## Editor Preview Screenshots:

Extreme Scale:
<img width="1919" height="1031" alt="Screenshot 2026-04-01 231259" src="https://github.com/user-attachments/assets/c5af0231-2844-4e48-9721-3608e652bd62" />

High Quality Effects:
<img width="1919" height="1029" alt="Screenshot 2026-02-12 015857" src="https://github.com/user-attachments/assets/7fe0f9b6-5a65-4160-ad74-874034a14ee6" />

Texturing System:
<img width="1919" height="1029" alt="Screenshot 2026-07-18 013607" src="https://github.com/user-attachments/assets/86c5ae53-8e12-4164-98dd-418f4ff5bae1" />

Multiple Workflows:
<img width="1919" height="1028" alt="Screenshot 2026-09-02 020709" src="https://github.com/user-attachments/assets/353f6bb6-527a-4336-99b2-01b55e936383" />

## Sponsor

If you find Division Engine useful, consider supporting development:

[![Sponsor](https://img.shields.io/badge/Sponsor-GitHub%20Sponsors-ea4aaa?style=flat&logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Rex-J-W)
